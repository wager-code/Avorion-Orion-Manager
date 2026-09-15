using System.Globalization;
using System.Text.Json;
using AvorionAdmin.Core;
using AvorionAdmin.Core.Abstractions;
using AvorionAdmin.Core.Configuration;
using AvorionAdmin.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace AvorionAdmin.Agent;

public sealed partial class SqliteStore
{
    public async Task<UpdateEnvironmentConfiguration?> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT steamcmd_path, server_directory, validated_at_utc FROM update_environment WHERE singleton_id = 1;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new UpdateEnvironmentConfiguration(
            reader.GetString(0),
            reader.GetString(1),
            DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal));
    }

    public async Task SaveAsync(UpdateEnvironmentConfiguration configuration, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO update_environment (singleton_id, steamcmd_path, server_directory, validated_at_utc)
            VALUES (1, $steamcmd, $server, $validated)
            ON CONFLICT(singleton_id) DO UPDATE SET
                steamcmd_path = excluded.steamcmd_path,
                server_directory = excluded.server_directory,
                validated_at_utc = excluded.validated_at_utc;
            """;
        command.Parameters.AddWithValue("$steamcmd", configuration.SteamCmdPath);
        command.Parameters.AddWithValue("$server", configuration.ServerDirectory);
        command.Parameters.AddWithValue("$validated", configuration.ValidatedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    async Task<ServerSetupDraftConfiguration?> IServerSetupDraftStore.GetAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT server_name, galaxy_name, galaxy_mode, max_players, galaxy_directory,
                   listen_address, game_port, query_port, rcon_enabled, rcon_port,
                   allow_firewall_change, install_management_mod, updated_at_utc
            FROM server_setup_draft WHERE singleton_id = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new ServerSetupDraftConfiguration(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt32(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetInt32(6),
            reader.GetInt32(7),
            reader.GetInt64(8) == 1,
            reader.GetInt32(9),
            reader.GetInt64(10) == 1,
            false,
            DateTimeOffset.Parse(reader.GetString(12), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal));
    }

    async Task IServerSetupDraftStore.SaveAsync(ServerSetupDraftConfiguration configuration, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO server_setup_draft
            (singleton_id, server_name, galaxy_name, galaxy_mode, max_players, galaxy_directory,
             listen_address, game_port, query_port, rcon_enabled, rcon_port,
             allow_firewall_change, install_management_mod, updated_at_utc)
            VALUES
            (1, $server_name, $galaxy_name, $galaxy_mode, $max_players, $galaxy_directory,
             $listen_address, $game_port, $query_port, $rcon_enabled, $rcon_port,
             $allow_firewall_change, $install_management_mod, $updated_at)
            ON CONFLICT(singleton_id) DO UPDATE SET
                server_name = excluded.server_name,
                galaxy_name = excluded.galaxy_name,
                galaxy_mode = excluded.galaxy_mode,
                max_players = excluded.max_players,
                galaxy_directory = excluded.galaxy_directory,
                listen_address = excluded.listen_address,
                game_port = excluded.game_port,
                query_port = excluded.query_port,
                rcon_enabled = excluded.rcon_enabled,
                rcon_port = excluded.rcon_port,
                allow_firewall_change = excluded.allow_firewall_change,
                install_management_mod = excluded.install_management_mod,
                updated_at_utc = excluded.updated_at_utc;
            """;
        command.Parameters.AddWithValue("$server_name", configuration.ServerName);
        command.Parameters.AddWithValue("$galaxy_name", configuration.GalaxyName);
        command.Parameters.AddWithValue("$galaxy_mode", configuration.GalaxyMode);
        command.Parameters.AddWithValue("$max_players", configuration.MaxPlayers);
        command.Parameters.AddWithValue("$galaxy_directory", configuration.GalaxyDirectory);
        command.Parameters.AddWithValue("$listen_address", configuration.ListenAddress);
        command.Parameters.AddWithValue("$game_port", configuration.GamePort);
        command.Parameters.AddWithValue("$query_port", configuration.QueryPort);
        command.Parameters.AddWithValue("$rcon_enabled", configuration.RconEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$rcon_port", configuration.RconPort);
        command.Parameters.AddWithValue("$allow_firewall_change", configuration.AllowFirewallChange ? 1 : 0);
        command.Parameters.AddWithValue("$install_management_mod", 0);
        command.Parameters.AddWithValue("$updated_at", configuration.UpdatedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

}
