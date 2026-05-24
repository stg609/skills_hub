using System.Net;

namespace SkillsHub.Api.GitLab;

public sealed class GitLabRequestException(HttpStatusCode statusCode, string? reasonPhrase)
    : HttpRequestException($"GitLab request failed: {(int)statusCode} {reasonPhrase}")
{
    public HttpStatusCode GitLabStatusCode { get; } = statusCode;
}
