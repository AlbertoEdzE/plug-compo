from ksquare.document_extraction.models import DocumentInput, ExtractedField


def test_document_input_requires_exactly_one_source():
    try:
        DocumentInput(content_type="application/pdf")
        assert False
    except ValueError as ex:
        assert "Exactly one" in str(ex)


def test_extracted_field_needs_review_threshold():
    assert ExtractedField(name="x", value="y", confidence=0.74).needs_review is True
    assert ExtractedField(name="x", value="y", confidence=0.75).needs_review is False
