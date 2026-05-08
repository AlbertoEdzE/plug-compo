using System.Collections.Concurrent;
using System.Data;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using KSquare.AuditTrail.Configuration;
using KSquare.AuditTrail.Extensions;
using KSquare.BlobStorage.Configuration;
using KSquare.BlobStorage.Contracts;
using KSquare.BlobStorage.Extensions;
using KSquare.Correlation.Extensions;
using KSquare.EventBus.Configuration;
using KSquare.EventBus.Contracts;
using KSquare.EventBus.Extensions;
using KSquare.EventBus.Models;
using KSquare.Idempotency.Configuration;
using KSquare.Idempotency.Extensions;
using KSquare.PolicyAdminAdapter.Configuration;
using KSquare.PolicyAdminAdapter.Contracts;
using KSquare.PolicyAdminAdapter.Extensions;
using KSquare.PolicyAdminAdapter.Models;
using KSquare.ProposalOrchestrator.Configuration;
using KSquare.ProposalOrchestrator.Contracts;
using KSquare.ProposalOrchestrator.Extensions;
using KSquare.ProposalOrchestrator.Models;
using KSquare.RatingAdapter.Configuration;
using KSquare.RatingAdapter.Contracts;
using KSquare.RatingAdapter.Extensions;
using KSquare.RatingAdapter.Models;
using KSquare.RulesEngine.Configuration;
using KSquare.RulesEngine.Extensions;
using KSquare.StateMachine.Configuration;
using KSquare.StateMachine.Contracts;
using KSquare.StateMachine.Definitions;
using KSquare.StateMachine.Extensions;
using KSquare.StateMachine.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var assertions = new List<AssertionRecord>();

var sql = Environment.GetEnvironmentVariable("LAB_SQL_CONNECTION")
          ?? "Server=localhost,1433;User ID=sa;Password=LocalDev_SA_Password123!;TrustServerCertificate=true;Encrypt=false;";
var redis = Environment.GetEnvironmentVariable("LAB_REDIS") ?? "localhost:6379";
var wiremock = Environment.GetEnvironmentVariable("LAB_WIREMOCK") ?? "http://localhost:8080";
var azurite = Environment.GetEnvironmentVariable("LAB_AZURITE_BLOB")
              ?? "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey="
              + "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;"
              + "BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;";

var synthJson = Environment.GetEnvironmentVariable("CANVAS5_SYNTH_JSON") ?? "";
Canvas5Synth? synth = null;
try
{
    synth = JsonSerializer.Deserialize<Canvas5Synth>(synthJson, new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    });
}
catch
{
    synth = null;
}

Record("synthesize_canvas5_inputs", synth is not null, "");

var eventSink = new EventSink();

using var host = Host.CreateDefaultBuilder()
    .ConfigureServices(services =>
    {
        services.AddLogging();

        services.AddKsCorrelation();

        services.AddKsEventBus(bus =>
        {
            bus.Provider = EventBusProvider.InMemory;
            bus.UseOutbox = false;
        });

        services.AddSingleton(eventSink);
        services.AddConsumer<StateTransitionedEvent, StateTransitionConsumer>("state-events", "lab");
        services.AddConsumer<ProposalGenerationCompletedEvent, ProposalCompletedConsumer>("proposal-events", "lab");
        services.AddConsumer<PolicyBoundEvent, PolicyBoundConsumer>("policy-events", "lab");

        services.AddKsAuditTrail(opts =>
        {
            opts.Provider = AuditProvider.SqlServer;
            opts.ConnectionString = sql;
            opts.ServiceName = "lab-canvas-5";
            opts.MaskPiiInBeforeAfter = true;
            opts.PiiFieldNames = new List<string> { "email", "phone" };
        });

        services.AddKsBlobStorage(opt =>
        {
            opt.Provider = BlobProvider.Azure;
            opt.ConnectionString = azurite;
        });

        services.AddKsIdempotency(o =>
        {
            o.Provider = IdempotencyProvider.Redis;
            o.RedisConnectionString = redis;
        });

        services.AddKsRatingAdapter(o =>
        {
            o.Provider = RatingProvider.Mock;
        });

        services.AddKsProposalOrchestrator(o =>
        {
            o.Provider = ProposalProvider.GhostDraft;
            o.ConnectionString = sql;
            o.GhostDraftApiUrl = wiremock;
            o.GhostDraftApiKey = "test";
            o.OutputBlobContainer = "generated-proposals";
            o.SasUrlTtl = TimeSpan.FromHours(1);
            o.PollingInterval = TimeSpan.FromMilliseconds(50);
            o.MaxPollingAttempts = 10;
            o.CompletionEventTopic = "proposal-events";
        });

        services.AddKsRulesEngine(rules =>
        {
            rules.RuleSource = RuleSetSource.EmbeddedYaml;
            rules.CacheTtl = TimeSpan.FromMinutes(1);
        }).AddRuleSet("bind-readiness");

        services.AddKsPolicyAdminAdapter(o =>
        {
            o.Provider = PolicyAdminProvider.Pcas;
            o.PcasBaseUrl = wiremock;
            o.PcasApiKey = "test";
            o.SqlConnectionString = sql;
            o.PollingInterval = TimeSpan.FromMilliseconds(50);
            o.MaxPollingAttempts = 50;
            o.BoundEventTopic = "policy-events";
            o.FailedEventTopic = "policy-events";
        });

        services.AddKsStateMachine(o =>
        {
            o.Provider = StateMachineProvider.Stateless;
            o.ConnectionString = sql;
            o.PublishTransitionEvents = true;
            o.TransitionEventTopic = "state-events";
            o.WriteAuditTrail = true;
            o.ConcurrencyRetryAttempts = 0;
        }).AddStateMachineDefinition<QuoteState, QuoteTrigger, QuoteStateMachineDefinition>();
    })
    .Build();

await Run("wiremock_stub_rating_proposal_pcas", async () =>
{
    if (synth is null)
    {
        throw new InvalidOperationException("Missing CANVAS5_SYNTH_JSON.");
    }

    using var http = new HttpClient { BaseAddress = new Uri(wiremock) };
    await http.PostAsync("/__admin/reset", content: null);
    await http.DeleteAsync("/__admin/requests");

    await http.PostAsJsonAsync("/__admin/mappings", new
    {
        request = new { method = "POST", urlPath = "/api/v3/documents/generate" },
        response = new
        {
            status = 200,
            headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
            jsonBody = new { jobId = synth.Quote.Wiremock.GhostDraftProviderJobId, status = "queued" }
        }
    });

    await http.PostAsJsonAsync("/__admin/mappings", new
    {
        request = new { method = "GET", urlPath = "/ghostdraft/download/proposal.pdf" },
        response = new
        {
            status = 200,
            headers = new Dictionary<string, string> { ["Content-Type"] = "application/pdf" },
            base64Body = Convert.ToBase64String(BuildMinimalPdfBytes())
        }
    });

    await http.PostAsJsonAsync("/__admin/mappings", new
    {
        request = new { method = "POST", urlPath = "/api/v2/policies/bind" },
        response = new
        {
            status = 200,
            headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
            jsonBody = new { transactionId = synth.Quote.Wiremock.PcasTransactionId, status = "submitted" }
        }
    });

    await http.PostAsJsonAsync("/__admin/mappings", new
    {
        request = new { method = "GET", urlPath = $"/api/v2/policies/{synth.Quote.Wiremock.PcasTransactionId}/status" },
        response = new
        {
            status = 200,
            headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
            jsonBody = new { status = "bound", policyNumber = synth.Quote.ExpectedPolicyNumber }
        }
    });
});

await EnsureAuditTrailSchemaAsync(sql);
await EnsureEfSchemasAsync(host.Services);

await host.StartAsync();

await Run("rating_mock_deterministic_premium", async () =>
{
    if (synth is null)
    {
        throw new InvalidOperationException("Missing CANVAS5_SYNTH_JSON.");
    }

    using var scope = host.Services.CreateScope();
    var adapter = scope.ServiceProvider.GetRequiredService<IRatingAdapter>();

    var request = new CoveragePricingRequest
    {
        SubmissionId = synth.Quote.SubmissionId,
        QuoteId = synth.Quote.QuoteId,
        InstitutionType = synth.Quote.InstitutionType,
        NaicsCode = synth.Quote.NaicsCode,
        State = synth.Quote.State,
        NumberOfLocations = synth.Quote.NumberOfLocations,
        TotalEnrollment = synth.Quote.TotalEnrollment,
        FteEmployees = synth.Quote.FteEmployees,
        TotalInsuredValue = synth.Quote.TotalInsuredValue,
        OperatingExpenses = synth.Quote.OperatingExpenses,
        EffectiveDate = DateOnly.Parse(synth.Quote.EffectiveDate),
        ExpirationDate = DateOnly.Parse(synth.Quote.ExpirationDate),
        CoverageLines = synth.Quote.CoverageLines.Select(l => new CoverageLineRequest
        {
            ProductCode = l.ProductCode,
            ProductName = l.ProductName,
            RequestedLimit = l.RequestedLimit,
            RequestedRetention = l.RequestedRetention,
            RequestedAggregateLimit = l.RequestedAggregateLimit
        }).ToList(),
        LossHistory = new LossHistorySummary
        {
            FiveYearAverageLossRatio = synth.Quote.LossHistory.FiveYearAverageLossRatio,
            LargestSingleLoss = synth.Quote.LossHistory.LargestSingleLoss,
            TotalClaimsCount = synth.Quote.LossHistory.TotalClaimsCount,
            DataYearsAvailable = synth.Quote.LossHistory.DataYearsAvailable
        },
        CorrelationId = "canvas5"
    };

    var result = await adapter.RequestPricingAsync(request);
    if (result.Status != RatingStatus.Rated)
    {
        throw new InvalidOperationException($"Expected Rated, got {result.Status}.");
    }

    foreach (var line in result.PremiumLines)
    {
        if (!synth.Quote.ExpectedPremium.ByProductCode.TryGetValue(line.ProductCode, out var expected))
        {
            throw new InvalidOperationException($"Missing expected premium for {line.ProductCode}.");
        }

        var diff = Math.Abs(line.AnnualPremium - expected);
        if (diff > 0.000001m)
        {
            throw new InvalidOperationException($"Premium mismatch for {line.ProductCode}. expected={expected} actual={line.AnnualPremium} diff={diff}");
        }
    }

    var totalDiff = Math.Abs(result.TotalAnnualPremium - synth.Quote.ExpectedPremium.TotalAnnualPremium);
    if (totalDiff > 0.000001m)
    {
        throw new InvalidOperationException($"Total premium mismatch. expected={synth.Quote.ExpectedPremium.TotalAnnualPremium} actual={result.TotalAnnualPremium}");
    }
});

await Run("quote_fsm_transitions_six_events_in_order", async () =>
{
    if (synth is null)
    {
        throw new InvalidOperationException("Missing CANVAS5_SYNTH_JSON.");
    }

    eventSink.StateTransitions.Clear();

    using var scope = host.Services.CreateScope();
    var factory = scope.ServiceProvider.GetRequiredService<IStateMachineFactory>();

    var machine = await factory.LoadAsync<QuoteState, QuoteTrigger>("Quote", synth.Quote.QuoteId, QuoteState.Draft);
    var ctx = new StateMachineContext
    {
        ActorId = synth.Quote.Underwriter.UserId,
        ActorName = synth.Quote.Underwriter.Name,
        Reason = "canvas5",
        CorrelationId = "canvas5"
    };

    await machine.FireAsync(QuoteTrigger.RequestPricing, ctx);
    await machine.FireAsync(QuoteTrigger.PricingComplete, ctx);
    await machine.FireAsync(QuoteTrigger.GenerateProposal, ctx);
    await machine.FireAsync(QuoteTrigger.ProposalReady, ctx);
    await machine.FireAsync(QuoteTrigger.Present, ctx);
    await machine.FireAsync(QuoteTrigger.Accept, ctx);

    if (machine.CurrentState != QuoteState.Accepted)
    {
        throw new InvalidOperationException($"Expected Accepted state, got {machine.CurrentState}.");
    }

    var events = eventSink.StateTransitions.ToArray();
    if (events.Length != 6)
    {
        throw new InvalidOperationException($"Expected 6 StateTransitionedEvent, got {events.Length}.");
    }

    var expected = new[]
    {
        QuoteTrigger.RequestPricing.ToString(),
        QuoteTrigger.PricingComplete.ToString(),
        QuoteTrigger.GenerateProposal.ToString(),
        QuoteTrigger.ProposalReady.ToString(),
        QuoteTrigger.Present.ToString(),
        QuoteTrigger.Accept.ToString(),
    };

    var actual = events.Select(e => e.Trigger).ToArray();
    if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
    {
        throw new InvalidOperationException($"Trigger order mismatch. actual={string.Join(",", actual)} expected={string.Join(",", expected)}");
    }

    var auditCount = await CountAuditEntriesAsync(sql, synth.Quote.QuoteId, action: "StateTransition");
    if (auditCount < 6)
    {
        throw new InvalidOperationException($"Expected at least 6 audit entries for quote transitions, got {auditCount}.");
    }
});

await Run("proposal_generation_stores_pdf_and_publishes_event", async () =>
{
    if (synth is null)
    {
        throw new InvalidOperationException("Missing CANVAS5_SYNTH_JSON.");
    }

    eventSink.Proposals.Clear();

    using var scope = host.Services.CreateScope();
    var proposals = scope.ServiceProvider.GetRequiredService<IProposalOrchestrator>();
    var blobs = scope.ServiceProvider.GetRequiredService<IBlobStorageConnector>();

    var request = new ProposalGenerationRequest
    {
        QuoteId = synth.Quote.QuoteId,
        SubmissionId = synth.Quote.SubmissionId,
        ProposalType = "QuoteProposal",
        InstitutionName = synth.Quote.InstitutionName,
        BrokerName = synth.Quote.Broker.Name,
        BrokerEmail = synth.Quote.Broker.Email,
        EffectiveDate = DateOnly.Parse(synth.Quote.EffectiveDate),
        ExpirationDate = DateOnly.Parse(synth.Quote.ExpirationDate),
        CoverageLines = synth.Quote.CoverageLines.Select(l => new ProposalCoverageLine
        {
            ProductName = l.ProductName,
            Limit = l.RequestedLimit,
            Retention = l.RequestedRetention,
            AnnualPremium = synth.Quote.ExpectedPremium.ByProductCode[l.ProductCode],
            AggregateLimit = l.RequestedAggregateLimit
        }).ToList(),
        UnderwriterName = synth.Quote.Underwriter.Name,
        OutputFormat = "pdf",
        CorrelationId = "canvas5"
    };

    var job = await proposals.StartGenerationAsync(request);
    var artifact = await proposals.CompleteJobAsync(job.JobId, $"{wiremock}/ghostdraft/download/proposal.pdf");

    var published = eventSink.Proposals.ToArray();
    if (published.Length != 1)
    {
        throw new InvalidOperationException($"Expected 1 ProposalGenerationCompletedEvent, got {published.Length}.");
    }

    if (string.IsNullOrWhiteSpace(artifact.BlobPath))
    {
        throw new InvalidOperationException("Expected proposal artifact blob path.");
    }

    var exists = await blobs.ExistsAsync(artifact.BlobPath);
    if (!exists)
    {
        throw new InvalidOperationException("Expected proposal blob to exist in Azurite.");
    }

    if (artifact.FileSizeBytes <= 0)
    {
        throw new InvalidOperationException("Expected proposal artifact file size > 0.");
    }
});

await Run("policy_bind_publishes_policy_bound_event_and_audit", async () =>
{
    if (synth is null)
    {
        throw new InvalidOperationException("Missing CANVAS5_SYNTH_JSON.");
    }

    eventSink.PolicyBounds.Clear();

    using var scope = host.Services.CreateScope();
    var adapter = scope.ServiceProvider.GetRequiredService<IPolicyAdminAdapter>();

    var req = new BindRequest
    {
        QuoteId = synth.Quote.QuoteId,
        SubmissionId = synth.Quote.SubmissionId,
        InstitutionLegalName = synth.Quote.InstitutionName,
        InstitutionDba = synth.Quote.InstitutionName,
        NaicsCode = synth.Quote.NaicsCode,
        InstitutionAddress = new Address
        {
            Line1 = "1 Main St",
            City = "Austin",
            State = synth.Quote.State,
            Zip = "78701",
        },
        ProducerLicenseNumber = "LIC-123",
        ProducerCode = "PROD-1",
        ProducerName = synth.Quote.Broker.Name,
        EffectiveDate = DateOnly.Parse(synth.Quote.EffectiveDate),
        ExpirationDate = DateOnly.Parse(synth.Quote.ExpirationDate),
        CoverageLines = synth.Quote.CoverageLines.Select(l => new BindCoverageLine
        {
            ProductCode = l.ProductCode,
            ProductName = l.ProductName,
            Limit = l.RequestedLimit,
            Retention = l.RequestedRetention,
            AnnualPremium = synth.Quote.ExpectedPremium.ByProductCode[l.ProductCode],
            AggregateLimit = l.RequestedAggregateLimit
        }).ToList(),
        TotalAnnualPremium = synth.Quote.ExpectedPremium.TotalAnnualPremium,
        BrokerEmail = synth.Quote.Broker.Email,
        UnderwriterUserId = synth.Quote.Underwriter.UserId,
        SpecialConditions = "none",
        CorrelationId = "canvas5"
    };

    var job = await adapter.SubmitBindAsync(req);

    var sw = new SpinWait();
    var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
    BindJob status = job;
    while (DateTimeOffset.UtcNow < deadline)
    {
        status = await adapter.GetBindStatusAsync(job.BindJobId);
        if (status.Status == BindJobStatus.Bound && !string.IsNullOrWhiteSpace(status.PolicyNumber))
        {
            break;
        }
        sw.SpinOnce();
        await Task.Delay(25);
    }

    if (status.Status != BindJobStatus.Bound || status.PolicyNumber != synth.Quote.ExpectedPolicyNumber)
    {
        throw new InvalidOperationException($"Expected policy bound {synth.Quote.ExpectedPolicyNumber}, got status={status.Status} policy={status.PolicyNumber ?? "<null>"}.");
    }

    var sw2 = new SpinWait();
    var deadline2 = DateTimeOffset.UtcNow.AddSeconds(2);
    while (DateTimeOffset.UtcNow < deadline2 && eventSink.PolicyBounds.Count < 1)
    {
        sw2.SpinOnce();
        await Task.Delay(25);
    }

    var evts = eventSink.PolicyBounds.ToArray();
    if (evts.Length != 1)
    {
        throw new InvalidOperationException($"Expected 1 PolicyBoundEvent, got {evts.Length}.");
    }

    var auditCount = await CountAuditEntriesAsync(sql, synth.Quote.QuoteId, action: "BindSubmitted");
    if (auditCount < 1)
    {
        throw new InvalidOperationException("Expected bind audit entry BindSubmitted.");
    }
});

await Run("invalid_transition_no_audit_no_event", async () =>
{
    if (synth is null)
    {
        throw new InvalidOperationException("Missing CANVAS5_SYNTH_JSON.");
    }

    eventSink.StateTransitions.Clear();

    using var scope = host.Services.CreateScope();
    var factory = scope.ServiceProvider.GetRequiredService<IStateMachineFactory>();
    var machine = await factory.LoadAsync<QuoteState, QuoteTrigger>("Quote", synth.Quote.QuoteId + "-invalid", QuoteState.Draft);

    var ctx = new StateMachineContext
    {
        ActorId = synth.Quote.Underwriter.UserId,
        ActorName = synth.Quote.Underwriter.Name,
        Reason = "canvas5-invalid",
        CorrelationId = "canvas5-invalid"
    };

    await machine.FireAsync(QuoteTrigger.RequestPricing, ctx);
    await machine.FireAsync(QuoteTrigger.PricingComplete, ctx);
    await machine.FireAsync(QuoteTrigger.GenerateProposal, ctx);
    await machine.FireAsync(QuoteTrigger.ProposalReady, ctx);
    await machine.FireAsync(QuoteTrigger.Present, ctx);
    await machine.FireAsync(QuoteTrigger.Accept, ctx);

    var beforeAudit = await CountAuditEntriesAsync(sql, synth.Quote.QuoteId + "-invalid", action: "StateTransition");
    var beforeEvents = eventSink.StateTransitions.Count;

    try
    {
        await machine.FireAsync(QuoteTrigger.PricingComplete, ctx);
        throw new InvalidOperationException("Expected InvalidTransitionException.");
    }
    catch (InvalidTransitionException)
    {
    }

    var afterAudit = await CountAuditEntriesAsync(sql, synth.Quote.QuoteId + "-invalid", action: "StateTransition");
    var afterEvents = eventSink.StateTransitions.Count;

    if (afterAudit != beforeAudit)
    {
        throw new InvalidOperationException("Audit trail changed after invalid transition.");
    }
    if (afterEvents != beforeEvents)
    {
        throw new InvalidOperationException("State transition events changed after invalid transition.");
    }
});

await Run("concurrent_bind_one_bound_one_concurrency_exception", async () =>
{
    if (synth is null)
    {
        throw new InvalidOperationException("Missing CANVAS5_SYNTH_JSON.");
    }

    eventSink.PolicyBounds.Clear();

    var quoteId = synth.Quote.QuoteId + "-concurrent";

    using (var scope = host.Services.CreateScope())
    {
        var factory = scope.ServiceProvider.GetRequiredService<IStateMachineFactory>();
        var machine = await factory.LoadAsync<QuoteState, QuoteTrigger>("Quote", quoteId, QuoteState.Draft);
        var ctx = new StateMachineContext
        {
            ActorId = synth.Quote.Underwriter.UserId,
            ActorName = synth.Quote.Underwriter.Name,
            Reason = "canvas5-concurrent",
            CorrelationId = "canvas5-concurrent"
        };

        await machine.FireAsync(QuoteTrigger.RequestPricing, ctx);
        await machine.FireAsync(QuoteTrigger.PricingComplete, ctx);
        await machine.FireAsync(QuoteTrigger.GenerateProposal, ctx);
        await machine.FireAsync(QuoteTrigger.ProposalReady, ctx);
        await machine.FireAsync(QuoteTrigger.Present, ctx);
    }

    var start = new TaskCompletionSource();
    var done = new ConcurrentBag<string>();
    var errors = new ConcurrentBag<Exception>();

    async Task AttemptAsync()
    {
        try
        {
            using var scope = host.Services.CreateScope();
            var factory = scope.ServiceProvider.GetRequiredService<IStateMachineFactory>();
            var machine = await factory.LoadAsync<QuoteState, QuoteTrigger>("Quote", quoteId, QuoteState.Draft);
            var ctx = new StateMachineContext
            {
                ActorId = synth.Quote.Underwriter.UserId,
                ActorName = synth.Quote.Underwriter.Name,
                Reason = "canvas5-concurrent",
                CorrelationId = "canvas5-concurrent"
            };

            await start.Task;
            await machine.FireAsync(QuoteTrigger.Accept, ctx);
            done.Add("accepted");

            var adapter = scope.ServiceProvider.GetRequiredService<IPolicyAdminAdapter>();
            var req = new BindRequest
            {
                QuoteId = quoteId,
                SubmissionId = synth.Quote.SubmissionId,
                InstitutionLegalName = synth.Quote.InstitutionName,
                InstitutionDba = synth.Quote.InstitutionName,
                NaicsCode = synth.Quote.NaicsCode,
                InstitutionAddress = new Address
                {
                    Line1 = "1 Main St",
                    City = "Austin",
                    State = synth.Quote.State,
                    Zip = "78701",
                },
                ProducerLicenseNumber = "LIC-123",
                ProducerCode = "PROD-1",
                ProducerName = synth.Quote.Broker.Name,
                EffectiveDate = DateOnly.Parse(synth.Quote.EffectiveDate),
                ExpirationDate = DateOnly.Parse(synth.Quote.ExpirationDate),
                CoverageLines = synth.Quote.CoverageLines.Select(l => new BindCoverageLine
                {
                    ProductCode = l.ProductCode,
                    ProductName = l.ProductName,
                    Limit = l.RequestedLimit,
                    Retention = l.RequestedRetention,
                    AnnualPremium = synth.Quote.ExpectedPremium.ByProductCode[l.ProductCode],
                    AggregateLimit = l.RequestedAggregateLimit
                }).ToList(),
                TotalAnnualPremium = synth.Quote.ExpectedPremium.TotalAnnualPremium,
                BrokerEmail = synth.Quote.Broker.Email,
                UnderwriterUserId = synth.Quote.Underwriter.UserId,
                SpecialConditions = "none",
                CorrelationId = "canvas5-concurrent"
            };

            _ = await adapter.SubmitBindAsync(req);
        }
        catch (Exception ex)
        {
            errors.Add(ex);
        }
    }

    var t1 = AttemptAsync();
    var t2 = AttemptAsync();
    start.SetResult();
    await Task.WhenAll(t1, t2);

    var concurrency = errors.OfType<ConcurrencyException>().Count();
    if (concurrency != 1)
    {
        throw new InvalidOperationException($"Expected exactly 1 ConcurrencyException, got {concurrency}.");
    }

    var sw = new SpinWait();
    var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
    while (DateTimeOffset.UtcNow < deadline && eventSink.PolicyBounds.Count < 1)
    {
        sw.SpinOnce();
        await Task.Delay(25);
    }

    if (eventSink.PolicyBounds.Count != 1)
    {
        throw new InvalidOperationException($"Expected 1 PolicyBoundEvent, got {eventSink.PolicyBounds.Count}.");
    }
});

await host.StopAsync();

var output = JsonSerializer.Serialize(new { assertions }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
Console.WriteLine(output);

void Record(string name, bool passed, string details) => assertions.Add(new AssertionRecord(name, passed, details));

async Task Run(string name, Func<Task> fn)
{
    try
    {
        await fn();
        Record(name, true, "");
    }
    catch (Exception ex)
    {
        Record(name, false, ex.Message);
    }
}

static byte[] BuildMinimalPdfBytes()
{
    return Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj\n<<>>\nendobj\ntrailer\n<<>>\n%%EOF\n");
}

static async Task EnsureAuditTrailSchemaAsync(string connectionString)
{
    var sql = """
              IF OBJECT_ID('audit_trail', 'U') IS NULL
              BEGIN
                  CREATE TABLE audit_trail (
                      entry_id        UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID() PRIMARY KEY,
                      resource_type   NVARCHAR(100) NOT NULL,
                      resource_id     NVARCHAR(500) NOT NULL,
                      action          NVARCHAR(200) NOT NULL,
                      actor_user_id   NVARCHAR(500) NOT NULL,
                      actor_name      NVARCHAR(500) NOT NULL,
                      actor_role      NVARCHAR(200) NULL,
                      actor_type      NVARCHAR(50) NOT NULL DEFAULT 'User',
                      before_json     NVARCHAR(MAX) NULL,
                      after_json      NVARCHAR(MAX) NULL,
                      correlation_id  NVARCHAR(200) NULL,
                      service_name    NVARCHAR(200) NULL,
                      tags_json       NVARCHAR(MAX) NULL,
                      occurred_at     DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
                  );

                  CREATE INDEX IX_audit_resource ON audit_trail (resource_type, resource_id, occurred_at DESC);
                  CREATE INDEX IX_audit_actor ON audit_trail (actor_user_id, occurred_at DESC);
                  CREATE INDEX IX_audit_occurred ON audit_trail (occurred_at DESC);
              END
              """;

    await using var conn = new SqlConnection(connectionString);
    await conn.OpenAsync();
    await using var cmd = conn.CreateCommand();
    cmd.CommandType = CommandType.Text;
    cmd.CommandText = sql;
    await cmd.ExecuteNonQueryAsync();
}

static async Task EnsureEfSchemasAsync(IServiceProvider sp)
{
    using var scope = sp.CreateScope();

    var proposalDb = scope.ServiceProvider.GetService<KSquare.ProposalOrchestrator.Database.ProposalDbContext>();
    if (proposalDb is not null)
    {
        await proposalDb.Database.EnsureCreatedAsync();
    }

    var policyDb = scope.ServiceProvider.GetService<KSquare.PolicyAdminAdapter.Database.PolicyAdminDbContext>();
    if (policyDb is not null)
    {
        await policyDb.Database.EnsureCreatedAsync();
    }

    var smDb = scope.ServiceProvider.GetService<KSquare.StateMachine.Database.StateMachineDbContext>();
    if (smDb is not null)
    {
        await smDb.Database.EnsureCreatedAsync();
    }
}

static async Task<int> CountAuditEntriesAsync(string connectionString, string quoteId, string action)
{
    await using var conn = new SqlConnection(connectionString);
    await conn.OpenAsync();

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = """
                      SELECT COUNT(*) FROM audit_trail
                      WHERE resource_type = 'Quote' AND resource_id = @id AND action = @action;
                      """;
    cmd.Parameters.AddWithValue("@id", quoteId);
    cmd.Parameters.AddWithValue("@action", action);

    var scalar = await cmd.ExecuteScalarAsync();
    return Convert.ToInt32(scalar);
}

sealed record AssertionRecord(string name, bool passed, string details);

sealed class EventSink
{
    public ConcurrentBag<StateTransitionedEvent> StateTransitions { get; } = new();
    public ConcurrentBag<ProposalGenerationCompletedEvent> Proposals { get; } = new();
    public ConcurrentBag<PolicyBoundEvent> PolicyBounds { get; } = new();
}

sealed class StateTransitionConsumer(EventSink sink) : IEventConsumer<StateTransitionedEvent>
{
    public Task ConsumeAsync(EventContext<StateTransitionedEvent> ctx, CancellationToken ct)
    {
        _ = ct;
        sink.StateTransitions.Add(ctx.Payload);
        return Task.CompletedTask;
    }
}

sealed class ProposalCompletedConsumer(EventSink sink) : IEventConsumer<ProposalGenerationCompletedEvent>
{
    public Task ConsumeAsync(EventContext<ProposalGenerationCompletedEvent> ctx, CancellationToken ct)
    {
        _ = ct;
        sink.Proposals.Add(ctx.Payload);
        return Task.CompletedTask;
    }
}

sealed class PolicyBoundConsumer(EventSink sink) : IEventConsumer<PolicyBoundEvent>
{
    public Task ConsumeAsync(EventContext<PolicyBoundEvent> ctx, CancellationToken ct)
    {
        _ = ct;
        sink.PolicyBounds.Add(ctx.Payload);
        return Task.CompletedTask;
    }
}

sealed record Canvas5Synth(Canvas5Quote Quote);

sealed record Canvas5Quote(
    string QuoteId,
    string SubmissionId,
    string InstitutionType,
    string InstitutionName,
    string NaicsCode,
    string State,
    int NumberOfLocations,
    int TotalEnrollment,
    int FteEmployees,
    decimal TotalInsuredValue,
    decimal OperatingExpenses,
    string EffectiveDate,
    string ExpirationDate,
    List<Canvas5CoverageLine> CoverageLines,
    Canvas5LossHistory LossHistory,
    Canvas5Broker Broker,
    Canvas5Underwriter Underwriter,
    Canvas5ExpectedPremium ExpectedPremium,
    string ExpectedPolicyNumber,
    Canvas5Wiremock Wiremock
);

sealed record Canvas5CoverageLine(string ProductCode, string ProductName, decimal RequestedLimit, decimal RequestedRetention, decimal? RequestedAggregateLimit);

sealed record Canvas5LossHistory(decimal FiveYearAverageLossRatio, decimal LargestSingleLoss, int TotalClaimsCount, int DataYearsAvailable);

sealed record Canvas5Broker(string Name, string Email);

sealed record Canvas5Underwriter(string UserId, string Name);

sealed record Canvas5ExpectedPremium(Dictionary<string, decimal> ByProductCode, decimal TotalAnnualPremium);

sealed record Canvas5Wiremock(string GhostDraftProviderJobId, string PcasTransactionId);
