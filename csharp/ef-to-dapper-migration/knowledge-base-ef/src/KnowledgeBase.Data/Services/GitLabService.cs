using System.Net.Http.Json;
using KnowledgeBase.Core.Interfaces;
using KnowledgeBase.Core.Models.GitLab;
using Microsoft.Extensions.Configuration;

namespace KnowledgeBase.Data.Services;

public class GitLabOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
}

public class GitLabService : IGitLabService
{
    private readonly HttpClient _http;

    public GitLabService(HttpClient http, IConfiguration config)
    {
        _http = http;

        var baseUrl = config["GitLab:BaseUrl"] ?? "https://gitlab.com";
        var projectId = config["GitLab:ProjectId"] ?? string.Empty;
        var token = config["GitLab:AccessToken"] ?? string.Empty;

        _http.BaseAddress = new Uri($"{baseUrl}/api/v4/projects/{projectId}/");
        _http.DefaultRequestHeaders.Add("PRIVATE-TOKEN", token);
    }

    public async Task<IEnumerable<GitLabIssueResponse>> FetchAllIssuesAsync(CancellationToken ct = default)
    {
        var all = new List<GitLabIssueResponse>();
        int page = 1;

        while (true)
        {
            var batch = await _http.GetFromJsonAsync<List<GitLabIssueResponse>>(
                $"issues?per_page=100&page={page}", ct);

            if (batch is null || batch.Count == 0) break;

            all.AddRange(batch);
            page++;
        }

        return all;
    }

    public async Task<IEnumerable<GitLabLabelResponse>> FetchAllLabelsAsync(CancellationToken ct = default)
    {
        var all = new List<GitLabLabelResponse>();
        int page = 1;

        while (true)
        {
            var batch = await _http.GetFromJsonAsync<List<GitLabLabelResponse>>(
                $"labels?per_page=100&page={page}", ct);

            if (batch is null || batch.Count == 0) break;

            all.AddRange(batch);
            page++;
        }

        return all;
    }
}
