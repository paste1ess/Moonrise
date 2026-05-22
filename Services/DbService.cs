using Microsoft.Data.Sqlite;
using Moonrise.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Moonrise.Services
{
    public class DbService : IDisposable
    {
        private readonly SqliteConnection _connection;
        public readonly string DbPath;

        public DbService(string path)
        {
            var connectionString = "Data Source=" + path;
            _connection = new SqliteConnection(connectionString);
            _connection.Open();

            using var walCommand = new SqliteCommand("PRAGMA journal_mode=WAL;", _connection);
            walCommand.ExecuteNonQuery();

            DbPath = path;

            InitSchema();
        }

        private void InitSchema()
        {
            using var songTableCommand = new SqliteCommand(@"CREATE TABLE IF NOT EXISTS tracks (
                id          TEXT PRIMARY KEY,
                album_id    TEXT NOT NULL,
                artist_id   TEXT NOT NULL,

                title       TEXT NOT NULL,
                album       TEXT NOT NULL,
                artist      TEXT NOT NULL,
                year        INTEGER,
                genre       TEXT,
                bpm         INTEGER,
                is_favorite INTEGER NOT NULL DEFAULT 0,

                file_path   TEXT NOT NULL,
                bitrate     INTEGER NOT NULL,
                duration    INTEGER NOT NULL,  -- stored in seconds 
                date_added  TEXT NOT NULL      -- stored as ISO8601 strings
            );", _connection);
            songTableCommand.ExecuteNonQuery();

            using var albumTableCommand = new SqliteCommand(@"CREATE TABLE IF NOT EXISTS albums (
                id          TEXT PRIMARY KEY,
                track_ids   TEXT NOT NULL,     -- JSON array
                artist_id   TEXT NOT NULL,

                title       TEXT NOT NULL,
                artist      TEXT NOT NULL,
                year        INTEGER,
                genre       TEXT,
                is_favorite INTEGER NOT NULL DEFAULT 0,

                date_added  TEXT NOT NULL      -- stored as ISO8601 strings
            );", _connection);
            albumTableCommand.ExecuteNonQuery();

            using var artistTableCommand = new SqliteCommand(@"CREATE TABLE IF NOT EXISTS artists (
                id          TEXT PRIMARY KEY,
                album_ids   TEXT NOT NULL,     -- JSON array

                name        TEXT NOT NULL,
                is_favorite INTEGER NOT NULL DEFAULT 0,

                date_added  TEXT NOT NULL      -- stored as ISO8601 strings
            );", _connection);
            artistTableCommand.ExecuteNonQuery();
        }

        public void ResetDb()
        {
            using var cmd = new SqliteCommand(@"
                DROP TABLE IF EXISTS tracks;
                DROP TABLE IF EXISTS albums;
                DROP TABLE IF EXISTS artists;
            ", _connection);
            cmd.ExecuteNonQuery();

            InitSchema();
        }

        public void UpsertTrack(Track track)
        {
            using var command = new SqliteCommand(@"INSERT OR REPLACE INTO tracks 
                (id, album_id, artist_id, title, album, artist, year, genre, bpm, is_favorite, file_path, bitrate, duration, date_added) VALUES 
                (@id, @album_id, @artist_id, @title, @album, @artist, @year, @genre, @bpm, @is_favorite, @file_path, @bitrate, @duration, @date_added);",
            _connection);

            command.Parameters.AddWithValue("@id", track.Id);
            command.Parameters.AddWithValue("@album_id", track.AlbumId);
            command.Parameters.AddWithValue("@artist_id", track.ArtistId);
            command.Parameters.AddWithValue("@title", track.Title);
            command.Parameters.AddWithValue("@album", track.Album);
            command.Parameters.AddWithValue("@artist", track.Artist);
            command.Parameters.AddWithValue("@year", (object?)track.Year ?? DBNull.Value);
            command.Parameters.AddWithValue("@genre", (object?)track.Genre ?? DBNull.Value);
            command.Parameters.AddWithValue("@bpm", (object?)track.Bpm ?? DBNull.Value);
            command.Parameters.AddWithValue("@is_favorite", track.IsFavorite ? 1 : 0);
            command.Parameters.AddWithValue("@file_path", track.FilePath);
            command.Parameters.AddWithValue("@bitrate", track.Bitrate);
            command.Parameters.AddWithValue("@duration", (int)track.Duration.TotalSeconds);
            command.Parameters.AddWithValue("@date_added", track.DateAdded.ToString("O"));

            command.ExecuteNonQuery();
        }
        public Track? GetTrack(string id)
        {
            using var command = new SqliteCommand("SELECT * FROM tracks WHERE id = @id", _connection);

            command.Parameters.AddWithValue("@id", id);

            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                return new Track
                {
                    Id = reader.GetString(reader.GetOrdinal("id")),
                    AlbumId = reader.GetString(reader.GetOrdinal("album_id")),
                    ArtistId = reader.GetString(reader.GetOrdinal("artist_id")),
                    Title = reader.GetString(reader.GetOrdinal("title")),
                    Album = reader.GetString(reader.GetOrdinal("album")),
                    Artist = reader.GetString(reader.GetOrdinal("artist")),

                    Year = reader.IsDBNull(reader.GetOrdinal("year")) ? null : reader.GetInt32(reader.GetOrdinal("year")),

                    Genre = reader.IsDBNull(reader.GetOrdinal("genre")) ? null : reader.GetString(reader.GetOrdinal("genre")),

                    IsFavorite = reader.GetBoolean(reader.GetOrdinal("is_favorite")),
                    FilePath = reader.GetString(reader.GetOrdinal("file_path")),
                    Bitrate = reader.GetInt32(reader.GetOrdinal("bitrate")),

                    Duration = TimeSpan.FromSeconds(reader.GetInt64(reader.GetOrdinal("duration"))),
                    DateAdded = DateTime.Parse(reader.GetString(reader.GetOrdinal("date_added")))
                };
            }
            return null;
        }

        public void InsertAlbum(Album album)
        {
            using var command = new SqliteCommand(@"INSERT INTO albums 
                (id, track_ids, artist_id, title, artist, year, genre, is_favorite, date_added) VALUES 
                (@id, @track_ids, @artist_id, @title, @artist, @year, @genre, @is_favorite, @date_added);",
            _connection);

            string jsonTracks = JsonSerializer.Serialize(album.TrackIds, DbJsonContext.Default.ListString);

            command.Parameters.AddWithValue("@id", album.Id);
            command.Parameters.AddWithValue("@track_ids", jsonTracks);
            command.Parameters.AddWithValue("@artist_id", album.ArtistId);
            command.Parameters.AddWithValue("@title", album.Title);
            command.Parameters.AddWithValue("@artist", album.Artist);
            command.Parameters.AddWithValue("@year", (object?)album.Year ?? DBNull.Value);
            command.Parameters.AddWithValue("@genre", (object?)album.Genre ?? DBNull.Value);
            command.Parameters.AddWithValue("@is_favorite", album.IsFavorite ? 1 : 0);
            command.Parameters.AddWithValue("@date_added", album.DateAdded.ToString("O"));

            command.ExecuteNonQuery();
        }

        public void InsertArtist(Artist artist)
        {
            using var command = new SqliteCommand(@"INSERT INTO artists 
                (id, album_ids, name, is_favorite, date_added) VALUES 
                (@id, @album_ids, @name, @is_favorite, @date_added);",
            _connection);

            string jsonAlbums = JsonSerializer.Serialize(artist.AlbumIds, DbJsonContext.Default.ListString);

            command.Parameters.AddWithValue("@id", artist.Id);
            command.Parameters.AddWithValue("@album_ids", jsonAlbums);
            command.Parameters.AddWithValue("@name", artist.Name);
            command.Parameters.AddWithValue("@is_favorite", artist.IsFavorite ? 1 : 0);
            command.Parameters.AddWithValue("@date_added", artist.DateAdded.ToString("O"));

            command.ExecuteNonQuery();
        }

        public void Dispose() => _connection?.Dispose();
    }
    public static class IdGenerator
    {
        public static string NewTrackId() => $"trk_{Guid.NewGuid():N}";
        public static string NewAlbumId() => $"alb_{Guid.NewGuid():N}";
        public static string NewArtistId() => $"art_{Guid.NewGuid():N}";
    }

    [JsonSerializable(typeof(List<string>))]
    internal partial class DbJsonContext : JsonSerializerContext { }
}
