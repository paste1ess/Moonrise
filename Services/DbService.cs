using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Data.Sqlite;
using Moonrise.Models;

namespace Moonrise.Services
{
    public class DbService : IDisposable
    {
        private readonly SqliteConnection _connection;
        
        public DbService(string connectionString)
        {
            _connection = new SqliteConnection(connectionString);
            _connection.Open();

            using var walCommand = new SqliteCommand("PRAGMA journal_mode=WAL;", _connection);
            walCommand.ExecuteNonQuery();

            using var songTableCommand = new SqliteCommand(@"CREATE TABLE tracks (
                id          TEXT PRIMARY KEY,
                album_id    TEXT NOT NULL,
                artist_id   TEXT NOT NULL,

                title       TEXT NOT NULL,
                album       TEXT NOT NULL,
                artist      TEXT NOT NULL,
                year        INTEGER,
                genre       TEXT,
                is_favorite INTEGER NOT NULL DEFAULT 0,

                file_path   TEXT NOT NULL,
                bitrate     INTEGER NOT NULL,
                duration    TEXT NOT NULL,     -- ISO8601 strings or ticks
                date_added  TEXT NOT NULL      -- stored as ISO8601 strings
            );", _connection);
            songTableCommand.ExecuteNonQuery();
        }

        public void InsertTrack(Track track)
        {
            using var command = new SqliteCommand(@"INSERT INTO tracks 
                (id, album_id, artist_id, title, album, artist, year, genre, is_favorite, file_path, bitrate, duration, date_added) VALUES
                (@id, @album_id, @artist_id, @title, @album, @artist, @year, @genre, @is_favorite, @file_path, @bitrate, @duration, @date_added);", 
            _connection);

            command.Parameters.AddWithValue("@id", track.Id);
            command.Parameters.AddWithValue("@album_id", track.AlbumId);
            command.Parameters.AddWithValue("@artist_id", track.ArtistId);
            command.Parameters.AddWithValue("@title", track.Title);
            command.Parameters.AddWithValue("@album", track.Album);
            command.Parameters.AddWithValue("@artist", track.Artist);
            command.Parameters.AddWithValue("@year", (object?)track.Year ?? DBNull.Value);
            command.Parameters.AddWithValue("@genre", (object?)track.Genre ?? DBNull.Value);
            command.Parameters.AddWithValue("@is_favorite", track.IsFavorite ? 1 : 0);
            command.Parameters.AddWithValue("@file_path", track.FilePath);
            command.Parameters.AddWithValue("@bitrate", track.Bitrate);
            command.Parameters.AddWithValue("@duration", track.Duration);
            command.Parameters.AddWithValue("@date_added", track.DateAdded);

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
}
