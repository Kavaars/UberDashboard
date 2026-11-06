using System.Text.Json;
using UberDashboard.Application.Abstractions;
using UberDashboard.Application.Synchronization;
using UberDashboard.Core.Synchronization;

namespace UberDashboard.Infrastructure.Database.Stores;

/// <summary>The document and outbox share one versioned row, so neither can commit without the other.</summary>
public sealed class SyncStore(IWorkQueue queue, SqliteDatabase database) : ISyncStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public Task<IReadOnlyList<SyncBinding>> ListAsync(CancellationToken token = default) => queue.EnqueueAsync<IReadOnlyList<SyncBinding>>(_ =>
    {
        database.EnsureInitialized();
        using var command = database.Connection.CreateCommand();
        command.CommandText = "SELECT BindingJson FROM SyncBinding ORDER BY BindingId;";
        using var reader = command.ExecuteReader();
        var result = new List<SyncBinding>();
        while (reader.Read()) result.Add(JsonSerializer.Deserialize<SyncBinding>(reader.GetString(0), Options)
            ?? throw new InvalidDataException("Invalid synchronization binding."));
        return result;
    }, token);

    public Task<SyncState?> ReadAsync(Guid bindingId, CancellationToken token = default) => queue.EnqueueAsync(_ =>
    {
        database.EnsureInitialized();
        using var command = database.Connection.CreateCommand();
        command.CommandText = "SELECT StateJson FROM SyncBinding WHERE BindingId = $id;";
        command.Parameters.AddWithValue("$id", bindingId.ToString("D"));
        return command.ExecuteScalar() is string json ? Read(json) : null;
    }, token);

    public Task<bool> SaveAsync(SyncState state, long expectedVersion, CancellationToken token = default)
    {
        if (expectedVersion < -1 || !SyncContainers.SupportedFormat(state)) throw new InvalidDataException("Unsupported synchronization format or version.");
        if (state.Binding.Id == Guid.Empty || string.IsNullOrWhiteSpace(state.Binding.ClientId) ||
            string.IsNullOrWhiteSpace(state.Binding.AccountId) || string.IsNullOrWhiteSpace(state.Binding.ResourcePath))
            throw new ArgumentException("A synchronization binding requires a stable identity.", nameof(state));
        return queue.EnqueueAsync(_ =>
        {
            ValidateEntities(state);
            database.EnsureInitialized();
            using var transaction = database.Connection.BeginTransaction();
            using var command = database.Connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = expectedVersion == -1
                ? """
                  INSERT INTO SyncBinding (BindingId, Provider, ClientId, AccountId, ResourcePath, BindingJson, Version, StateJson)
                  VALUES ($id, $provider, $client, $account, $path, $binding, $version, $state)
                  ON CONFLICT DO NOTHING;
                  """
                : """
                  UPDATE SyncBinding SET Version = $version, StateJson = $state, BindingJson = $binding
                  WHERE BindingId = $id AND Version = $expected AND Provider = $provider
                    AND ClientId = $client AND AccountId = $account AND ResourcePath = $path;
                  """;
            command.Parameters.AddWithValue("$id", state.Binding.Id.ToString("D"));
            command.Parameters.AddWithValue("$provider", (int)state.Binding.Provider);
            command.Parameters.AddWithValue("$client", state.Binding.ClientId);
            command.Parameters.AddWithValue("$account", state.Binding.AccountId);
            command.Parameters.AddWithValue("$path", state.Binding.ResourcePath);
            command.Parameters.AddWithValue("$binding", JsonSerializer.Serialize(state.Binding, Options));
            command.Parameters.AddWithValue("$version", checked(expectedVersion + 1));
            command.Parameters.AddWithValue("$state", JsonSerializer.Serialize(state with { Version = expectedVersion + 1 }, Options));
            if (expectedVersion != -1) command.Parameters.AddWithValue("$expected", expectedVersion);
            var saved = command.ExecuteNonQuery() == 1;
            transaction.Commit();
            return saved;
        }, token);
    }

    private static SyncState Read(string json)
    {
        var state = JsonSerializer.Deserialize<SyncState>(json, Options)
            ?? throw new InvalidDataException("Invalid synchronization state.");
        if (!SyncContainers.SupportedFormat(state)) throw new InvalidDataException("Unsupported synchronization state format.");
        ValidateEntities(state);
        return state;
    }

    private static void ValidateEntities(SyncState state)
    {
        var ids = state.Items.Select(e => e.LocalId).ToHashSet();
        var parents = state.Items.Where(e => e.Kind == SyncEntityKind.Item).Select(e => e.LocalId).ToHashSet();
        if (ids.Contains(Guid.Empty) || ids.Count != state.Items.Count || state.Queue.Any(o => !ids.Contains(o.EntityId)) ||
            state.Items.Any(e => !Enum.IsDefined(e.Kind) || e.Kind == SyncEntityKind.Item && e.ParentId is not null ||
                e.Kind != SyncEntityKind.Item && (state.Binding.Provider != SyncProviderKind.MicrosoftTodo ||
                    MicrosoftTodoRelations.TaskCollectionPath(state.Binding.ResourcePath) != state.Binding.ResourcePath ||
                    !parents.Contains(e.ParentId ?? Guid.Empty))))
            throw new InvalidDataException("Invalid synchronization item identities or parent relationships.");
        if (state.FileTransfers.Select(t => t.Id).Distinct().Count() != state.FileTransfers.Count ||
            state.FileTransfers.Count > 0 && !SyncFileRules.IsContentBinding(state.Binding) ||
            state.FileTransfers.Any(t => t.Id == Guid.Empty || !parents.Contains(t.EntityId) || !SyncFileRules.Valid(t.File, state.Binding.Provider) ||
                !Enum.IsDefined(t.State) || !Enum.IsDefined(t.Stage) || t.Uploaded < 0 || t.Uploaded > t.File.Size ||
                t.ProtectedSession is not null && !t.ProtectedSession.StartsWith("dpapi1:", StringComparison.Ordinal)))
            throw new InvalidDataException("Invalid transfer identity or checkpoint.");
        if (state.SeriesPlans.Select(p => p.Id).Distinct().Count() != state.SeriesPlans.Count ||
            state.SeriesPlans.Count > 0 && (state.Binding.Provider != SyncProviderKind.GoogleCalendar || !SyncFileRules.IsContentBinding(state.Binding)) ||
            state.SeriesPlans.Any(p => p.Id == Guid.Empty || !parents.Contains(p.EntityId) || !Enum.IsDefined(p.State) || !Enum.IsDefined(p.Stage) ||
                p.FollowingId.Length != 32 || p.FollowingId.Any(c => !Uri.IsHexDigit(c)) || string.IsNullOrWhiteSpace(p.InstanceId)) ||
            state.InstanceWindows.Any(w => state.Binding.Provider != SyncProviderKind.GoogleCalendar || !SyncFileRules.IsContentBinding(state.Binding) ||
                string.IsNullOrWhiteSpace(w.SeriesId) || w.Until <= w.From || w.Until - w.From > TimeSpan.FromDays(3660)))
            throw new InvalidDataException("Invalid series workflow or occurrence cache.");
    }
}
