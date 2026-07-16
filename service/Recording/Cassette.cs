namespace CmProjectX.Recording;

// One recorded HTTP interaction (a "cassette"): the request identity plus the
// response to replay for it. Auth headers and cookies are intentionally NOT
// captured — sanitization by construction, so a committed cassette can never
// carry a bearer token. Body-level PII redaction (GUIDs, serials) is a later
// pass (see docs/REGRESSION-TESTING.md, RT2).
public sealed record Cassette(CassetteRequest Request, CassetteResponse Response);

public sealed record CassetteRequest(string Method, string Url);

public sealed record CassetteResponse(int Status, string? ContentType, string Body);
