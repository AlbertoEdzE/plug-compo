from __future__ import annotations

from dataclasses import dataclass

from .base_synthesizer import BaseSynthesizer


@dataclass(frozen=True)
class SynthesizedEmail:
    mailbox: str
    graph_message_id: str
    message_id_header: str
    raw_mime: str
    attachment_names: list[str]


class EmailSynthesizer(BaseSynthesizer):
    def mailbox(self) -> str:
        return "submissions@company.test"

    def graph_message_id(self) -> str:
        return f"graph-{self.faker.uuid4()}"

    def message_id_header(self) -> str:
        return f"<{self.faker.uuid4()}@company.test>"

    def synthesized_mime_with_two_attachments(self) -> SynthesizedEmail:
        mailbox = self.mailbox()
        graph_id = self.graph_message_id()
        msg_id = self.message_id_header()

        boundary = "----=_Part_123456_789012345.1700000000000"
        attachment1_name = "acord125.pdf"
        attachment2_name = "loss_run.xlsx"

        attachment1_payload = "SGVsbG8gQUNPUkQgMTI1"  # "Hello ACORD 125"
        attachment2_payload = "SGVsbG8gTG9zcyBSdW4="  # "Hello Loss Run"

        subject = f"New submission — {self.faker.company()}"
        body = "Please find the submission attached."

        raw = (
            f"Message-ID: {msg_id}\r\n"
            f"Date: Tue, 01 Sep 2026 12:00:00 +0000\r\n"
            f"From: Broker <broker@broker.test>\r\n"
            f"To: {mailbox}\r\n"
            f"Subject: {subject}\r\n"
            f"MIME-Version: 1.0\r\n"
            f"Content-Type: multipart/mixed; boundary=\"{boundary}\"\r\n"
            f"\r\n"
            f"--{boundary}\r\n"
            f"Content-Type: text/plain; charset=\"utf-8\"\r\n"
            f"\r\n"
            f"{body}\r\n"
            f"\r\n"
            f"--{boundary}\r\n"
            f"Content-Type: application/pdf\r\n"
            f"Content-Disposition: attachment; filename=\"{attachment1_name}\"\r\n"
            f"Content-Transfer-Encoding: base64\r\n"
            f"\r\n"
            f"{attachment1_payload}\r\n"
            f"\r\n"
            f"--{boundary}\r\n"
            f"Content-Type: application/vnd.openxmlformats-officedocument.spreadsheetml.sheet\r\n"
            f"Content-Disposition: attachment; filename=\"{attachment2_name}\"\r\n"
            f"Content-Transfer-Encoding: base64\r\n"
            f"\r\n"
            f"{attachment2_payload}\r\n"
            f"\r\n"
            f"--{boundary}--\r\n"
        )

        return SynthesizedEmail(
            mailbox=mailbox,
            graph_message_id=graph_id,
            message_id_header=msg_id,
            raw_mime=raw,
            attachment_names=[attachment1_name, attachment2_name],
        )

    def sendgrid_model(self) -> dict:
        return {
            "SubmissionNumber": f"SUB-{self.faker.random_int(min=1000, max=9999)}",
            "BrokerName": "Jane Smith",
            "OldStatus": "Draft",
            "NewStatus": "Submitted",
            "PortalUrl": "https://uw.company.test/submissions/1234",
        }

    def notification_recipient(self) -> dict:
        return {
            "UserId": self.faker.uuid4().replace("-", ""),
            "Email": self.faker.email(),
            "DisplayName": self.faker.name(),
        }
