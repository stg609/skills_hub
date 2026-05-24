using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SkillsHub.Api.GitLab;

public sealed record GitLabProject(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("ssh_url_to_repo")] string? SshUrlToRepo,
    [property: JsonPropertyName("http_url_to_repo")] string? HttpUrlToRepo,
    [property: JsonPropertyName("default_branch")] string DefaultBranch,
    [property: JsonPropertyName("last_activity_at")] DateTimeOffset LastActivityAt);

public sealed record GitLabTreeItem(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("size")] long? Size);

public sealed class GitLabClient
{
    private readonly HttpClient _http;
    private readonly AppConfig _config;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public GitLabClient(HttpClient http, AppConfig config)
    {
        _http = http;
        _config = config;
        _http.BaseAddress = new Uri(config.GitLabBaseUrl.TrimEnd('/') + "/");
        _http.DefaultRequestHeaders.Add("PRIVATE-TOKEN", config.GitLabToken);
    }

    public async Task<IReadOnlyList<GitLabProject>> ListGroupProjectsAsync(string groupPath, CancellationToken cancellationToken)
    {
        var projects = new List<GitLabProject>();
        for (var page = 1; ; page++)
        {
            var encodedGroup = Uri.EscapeDataString(groupPath);
            var batch = await GetJsonAsync<List<GitLabProject>>(
                $"api/v4/groups/{encodedGroup}/projects?include_subgroups=true&simple=true&per_page=100&page={page}",
                cancellationToken);
            projects.AddRange(batch);
            if (batch.Count < 100) break;
        }
        return projects;
    }

    public async IAsyncEnumerable<GitLabProject> EnumerateGroupProjectsAsync(string groupPath, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var page = 1; ; page++)
        {
            var encodedGroup = Uri.EscapeDataString(groupPath);
            var batch = await GetJsonAsync<List<GitLabProject>>(
                $"api/v4/groups/{encodedGroup}/projects?include_subgroups=true&simple=true&per_page=100&page={page}",
                cancellationToken);
            foreach (var project in batch) yield return project;
            if (batch.Count < 100) break;
        }
    }

    public async Task<IReadOnlyList<GitLabTreeItem>> ListRepositoryTreeAsync(long projectId, string reference, string path, CancellationToken cancellationToken)
    {
        var items = new List<GitLabTreeItem>();
        for (var page = 1; ; page++)
        {
            var query = $"recursive=true&per_page=100&page={page}&ref={Uri.EscapeDataString(reference)}";
            if (!string.IsNullOrWhiteSpace(path)) query += $"&path={Uri.EscapeDataString(path)}";
            var batch = await GetJsonAsync<List<GitLabTreeItem>>(
                $"api/v4/projects/{projectId}/repository/tree?{query}",
                cancellationToken);
            items.AddRange(batch);
            if (batch.Count < 100) break;
        }
        return items;
    }

    public async IAsyncEnumerable<GitLabTreeItem> EnumerateRepositoryTreeAsync(long projectId, string reference, string path, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var page = 1; ; page++)
        {
            var query = $"recursive=true&per_page=100&page={page}&ref={Uri.EscapeDataString(reference)}";
            if (!string.IsNullOrWhiteSpace(path)) query += $"&path={Uri.EscapeDataString(path)}";
            var batch = await GetJsonAsync<List<GitLabTreeItem>>(
                $"api/v4/projects/{projectId}/repository/tree?{query}",
                cancellationToken);
            foreach (var item in batch) yield return item;
            if (batch.Count < 100) break;
        }
    }

    public async Task<string> GetRawFileAsync(long projectId, string filePath, string reference, CancellationToken cancellationToken)
    {
        var response = await SendAsync(
            $"api/v4/projects/{projectId}/repository/files/{Uri.EscapeDataString(filePath)}/raw?ref={Uri.EscapeDataString(reference)}",
            cancellationToken);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task<byte[]> GetRawFileBytesLimitedAsync(long projectId, string filePath, string reference, long maxBytes, CancellationToken cancellationToken)
    {
        if (maxBytes < 0)
            throw new InvalidOperationException("skill package is too large");

        var response = await SendAsync(
            $"api/v4/projects/{projectId}/repository/files/{Uri.EscapeDataString(filePath)}/raw?ref={Uri.EscapeDataString(reference)}",
            cancellationToken);

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (output.Length + read > maxBytes)
                throw new InvalidOperationException("skill package is too large");
            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private async Task<T> GetJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        var response = await SendAsync(path, cancellationToken);
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
               ?? throw new InvalidOperationException("GitLab returned empty JSON.");
    }

    private async Task<HttpResponseMessage> SendAsync(string path, CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt <= _config.GitLabRequestRetries; attempt++)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_config.GitLabRequestTimeoutMs);

            try
            {
                var response = await _http.GetAsync(path, timeout.Token);
                if (response.IsSuccessStatusCode) return response;
                if (!IsRetryable(response.StatusCode) || attempt == _config.GitLabRequestRetries)
                    throw new GitLabRequestException(response.StatusCode, response.ReasonPhrase);
            }
            catch (Exception error) when (IsRetryableException(error) && attempt < _config.GitLabRequestRetries)
            {
                lastError = error;
            }

            // 中文注释：GitLab 偶发 429/5xx 时短暂退避，避免一次抖动导致整轮索引失败。
            await Task.Delay(TimeSpan.FromMilliseconds(200 * (attempt + 1)), cancellationToken);
        }

        throw lastError ?? new InvalidOperationException("GitLab request failed.");
    }

    private static bool IsRetryable(HttpStatusCode status) =>
        status == HttpStatusCode.TooManyRequests || (int)status >= 500;

    private static bool IsRetryableException(Exception error) =>
        error is TaskCanceledException or HttpRequestException;
}
