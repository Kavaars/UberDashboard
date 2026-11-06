using System.Net;
using System.Text.RegularExpressions;
using UberDashboard.Application.Synchronization;
using UberDashboard.Core.Synchronization;

namespace UberDashboard.Infrastructure.Integrations;

public sealed partial class MicrosoftTodoProvider(HttpClient http, IIntegrationAccountService accounts)
    : SyncHttpProvider(http, accounts), IMicrosoftTodoProvider, ISyncRelationProvider
{
    public override SyncProviderKind Kind => SyncProviderKind.MicrosoftTodo;
    public override IReadOnlyList<string[]> AtomicGroups { get; } = [["startDateTime", "dueDateTime"], ["recurrence"], ["isReminderOn", "reminderDateTime"], ["isChecked", "checkedDateTime"]];
    protected override Uri Root { get; } = new("https://graph.microsoft.com/v1.0/me/todo/");
    protected override bool ValidPath(string path) => ResourcePath().IsMatch(path);
    protected override IReadOnlySet<string> WritableFields(string path) => path.EndsWith("/checklistItems", StringComparison.Ordinal)
        ? new HashSet<string>(["displayName", "isChecked", "checkedDateTime", "createdDateTime"], StringComparer.Ordinal)
        : path.EndsWith("/linkedResources", StringComparison.Ordinal)
            ? new HashSet<string>(["webUrl", "applicationName", "displayName", "externalId"], StringComparer.Ordinal)
            : path == "lists" ? new HashSet<string>(["displayName"], StringComparer.Ordinal)
            : new HashSet<string>(["title", "body", "status", "importance", "categories", "startDateTime", "dueDateTime", "completedDateTime", "isReminderOn", "reminderDateTime", "recurrence"], StringComparer.Ordinal);

    public override async Task<SyncPage> ReadPageAsync(SyncBinding binding, string? cursor, string? page, CancellationToken token)
    {
        var path = binding.ResourcePath;
        var delta = path == "lists" || path.EndsWith("/tasks", StringComparison.Ordinal);
        var uri = page is not null ? Trusted(new Uri(page, UriKind.Absolute)) : cursor is not null
            ? Trusted(new Uri(cursor, UriKind.Absolute)) : new Uri(CollectionUri(binding).AbsoluteUri + (delta ? "/delta" : ""));
        if (uri.AbsolutePath != CollectionUri(binding).AbsolutePath + (delta ? "/delta" : ""))
            throw new InvalidDataException("Continuation does not belong to this collection.");
        using var response = await SendAsync(binding, HttpMethod.Get, uri, null, null, false, token).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Gone) throw new SyncCursorExpiredException();
        await EnsureReadAsync(response).ConfigureAwait(false);
        var body = await BodyAsync(response, token).ConfigureAwait(false);
        if (body["value"] is not System.Text.Json.Nodes.JsonArray items) throw new InvalidDataException("Missing collection.");
        var entries = new List<SyncRemoteEntity>();
        foreach (var node in items)
        {
            var entry = Parse(binding, node!.AsObject());
            // Delta payloads are not treated as replacements for properties omitted by the server.
            if (delta && entry.Fields is not null)
                entry = await GetAsync(binding, entry.Id, token).ConfigureAwait(false) ?? new(entry.Id, null);
            entries.Add(entry);
        }
        return new(entries, body["@odata.nextLink"]?.GetValue<string>(),
            body["@odata.deltaLink"]?.GetValue<string>(), !delta || cursor is null);
    }

    public async Task<IReadOnlyList<SyncRelationSnapshot>> ReadRelationsAsync(SyncBinding binding, IReadOnlyList<SyncEntity> parents, CancellationToken token)
    {
        _ = CollectionUri(binding);
        if (MicrosoftTodoRelations.TaskCollectionPath(binding.ResourcePath) != binding.ResourcePath) return [];
        var result = new List<SyncRelationSnapshot>();
        foreach (var parent in parents)
        {
            foreach (var kind in new[] { SyncEntityKind.ChecklistItem, SyncEntityKind.LinkedResource })
            {
                var child = new SyncEntity(Guid.Empty, null, null, null, Kind: kind, ParentId: parent.LocalId);
                var scope = MicrosoftTodoRelations.Scope(new(binding, Entities: [parent]), child);
                var items = new List<SyncRemoteEntity>(); var pages = new HashSet<string>(StringComparer.Ordinal); string? page = null;
                do
                {
                    var response = await ReadPageAsync(scope, null, page, token).ConfigureAwait(false);
                    items.AddRange(response.Items); page = response.NextPage;
                    if (page is not null && (!pages.Add(page) || pages.Count > 10000)) throw new InvalidDataException("Invalid related-item pagination.");
                } while (page is not null);
                result.Add(new(parent.LocalId, kind, items));
            }
        }
        return result;
    }

    protected override SyncWriteResult? ValidateWrite(SyncBinding binding, SyncOperation operation, SyncRemoteEntity? current, System.Text.Json.Nodes.JsonObject? patch)
    {
        if (binding.ResourcePath != "lists" || current is null) return null;
        return (operation.Kind == SyncOperationKind.Delete ? SyncContainers.CanDelete(Kind, current.Metadata) : SyncContainers.CanEdit(Kind, current.Metadata))
            ? null : new(SyncWriteStatus.Forbidden, ErrorCode: "list-system-or-not-owned");
    }

    [GeneratedRegex("^lists(?:/(?:[A-Za-z0-9_+=@.-]|%[0-9a-fA-F]{2})+/tasks(?:/(?:[A-Za-z0-9_+=@.-]|%[0-9a-fA-F]{2})+/(?:checklistItems|linkedResources))?)?$", RegexOptions.CultureInvariant)]
    private static partial Regex ResourcePath();
}
