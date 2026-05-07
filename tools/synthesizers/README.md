# Synthesizers Convention

Synthesizers are small utilities used by tests to generate deterministic input data.
They exist to enforce the "no hardcoded test data" policy across the repository.

Rules:
- Do not commit generated output files.
- Seed the generator with a fixed integer so the same run produces the same data.
- Keep synthesizers close to the tests that use them unless the data is shared cross-component.

Language conventions:
- C#: use Bogus with a fixed seed (for example, Randomizer.Seed).
- Python: use faker with a fixed seed (for example, Faker.seed()).

Output:
- If a synthesizer writes artifacts (CSV/JSON/etc.), write them under synthesized-data/.
- synthesized-data/ is ignored by git.
