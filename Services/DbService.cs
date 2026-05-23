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
        private readonly object _dbLock = new();
        public readonly string DbPath;

        public DbService(string path)
        {
            var connectionString = "Data Source=" + path;
            _connection = new SqliteConnection(connectionString);
            _connection.Open();

            using var walCommand = new SqliteCommand("PRAGMA journal_mode=WAL;", _connection);
            walCommand.ExecuteNonQuery();

            using var cacheSizeCommand = new SqliteCommand("PRAGMA cache_size=-2000;", _connection);
            cacheSizeCommand.ExecuteNonQuery();

            DbPath = path;

            InitSchema();
        }

        private void InitSchema()
        {
            lock (_dbLock)
            {
                using var songTableCommand = new SqliteCommand(@"CREATE TABLE IF NOT EXISTS tracks (
                    id            TEXT PRIMARY KEY,
                    album_id      TEXT NOT NULL,
                    artist_id     TEXT NOT NULL,
                    title         TEXT NOT NULL,
                    album         TEXT NOT NULL,
                    artist        TEXT NOT NULL,
                    year          INTEGER,
                    genre         TEXT,
                    bpm           INTEGER,
                    is_favorite   INTEGER NOT NULL DEFAULT 0,
                    file_path     TEXT NOT NULL,
                    file_size     INTEGER NOT NULL DEFAULT 0,
                    bitrate       INTEGER NOT NULL,
                    duration      INTEGER NOT NULL,
                    date_added    TEXT NOT NULL,
                    last_modified TEXT NOT NULL DEFAULT '',
                    is_present    INTEGER NOT NULL DEFAULT 1
                );", _connection);
                songTableCommand.ExecuteNonQuery();

                using var albumTableCommand = new SqliteCommand(@"CREATE TABLE IF NOT EXISTS albums (
                    id          TEXT PRIMARY KEY,
                    track_ids   TEXT NOT NULL,
                    artist_id   TEXT NOT NULL,
                    title       TEXT NOT NULL,
                    artist      TEXT NOT NULL,
                    year        INTEGER,
                    genre       TEXT,
                    is_favorite INTEGER NOT NULL DEFAULT 0,
                    date_added  TEXT NOT NULL
                );", _connection);
                albumTableCommand.ExecuteNonQuery();

                using var artistTableCommand = new SqliteCommand(@"CREATE TABLE IF NOT EXISTS artists (
                    id          TEXT PRIMARY KEY,
                    album_ids   TEXT NOT NULL,
                    name        TEXT NOT NULL,
                    is_favorite INTEGER NOT NULL DEFAULT 0,
                    date_added  TEXT NOT NULL
                );", _connection);
                artistTableCommand.ExecuteNonQuery();
            }
        }

        public void ResetDb()
        {
            lock (_dbLock)
            {
                using var cmd = new SqliteCommand(@"
                    DROP TABLE IF EXISTS tracks;
                    DROP TABLE IF EXISTS albums;
                    DROP TABLE IF EXISTS artists;
                ", _connection);
                cmd.ExecuteNonQuery();

                InitSchema();
            }
        }

        public void UpsertTrack(Track track)
        {
            lock (_dbLock)
            {
                using var command = new SqliteCommand(@"INSERT OR REPLACE INTO tracks 
                    (id, album_id, artist_id, title, album, artist, year, genre, bpm, is_favorite, file_path, file_size, bitrate, duration, date_added, last_modified, is_present) VALUES 
                    (@id, @album_id, @artist_id, @title, @album, @artist, @year, @genre, @bpm, @is_favorite, @file_path, @file_size, @bitrate, @duration, @date_added, @last_modified, @is_present);",
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
                command.Parameters.AddWithValue("@file_size", track.FileSize);
                command.Parameters.AddWithValue("@bitrate", track.Bitrate);
                command.Parameters.AddWithValue("@duration", (int)track.Duration.TotalSeconds);
                command.Parameters.AddWithValue("@date_added", track.DateAdded.ToString("O"));
                command.Parameters.AddWithValue("@last_modified", track.LastModified);
                command.Parameters.AddWithValue("@is_present", track.IsPresent ? 1 : 0);

                command.ExecuteNonQuery();
            }
        }

        public void UpsertTracksBatch(IEnumerable<Track> tracks)
        {
            lock (_dbLock)
            {
                using var transaction = _connection.BeginTransaction();
                using var command = new SqliteCommand(@"INSERT OR REPLACE INTO tracks 
            (id, album_id, artist_id, title, album, artist, year, genre, bpm, is_favorite, file_path, file_size, bitrate, duration, date_added, last_modified, is_present) VALUES 
            (@id, @album_id, @artist_id, @title, @album, @artist, @year, @genre, @bpm, @is_favorite, @file_path, @file_size, @bitrate, @duration, @date_added, @last_modified, @is_present);",
                _connection, transaction);

                var idParam = command.Parameters.Add("@id", SqliteType.Text);
                var albumIdParam = command.Parameters.Add("@album_id", SqliteType.Text);
                var artistIdParam = command.Parameters.Add("@artist_id", SqliteType.Text);
                var titleParam = command.Parameters.Add("@title", SqliteType.Text);
                var albumParam = command.Parameters.Add("@album", SqliteType.Text);
                var artistParam = command.Parameters.Add("@artist", SqliteType.Text);
                var yearParam = command.Parameters.Add("@year", SqliteType.Integer);
                var genreParam = command.Parameters.Add("@genre", SqliteType.Text);
                var bpmParam = command.Parameters.Add("@bpm", SqliteType.Integer);
                var isFavoriteParam = command.Parameters.Add("@is_favorite", SqliteType.Integer);
                var filePathParam = command.Parameters.Add("@file_path", SqliteType.Text);
                var fileSizeParam = command.Parameters.Add("@file_size", SqliteType.Integer);
                var bitrateParam = command.Parameters.Add("@bitrate", SqliteType.Integer);
                var durationParam = command.Parameters.Add("@duration", SqliteType.Integer);
                var dateAddedParam = command.Parameters.Add("@date_added", SqliteType.Text);
                var lastModifiedParam = command.Parameters.Add("@last_modified", SqliteType.Text);
                var isPresentParam = command.Parameters.Add("@is_present", SqliteType.Integer);

                foreach (var track in tracks)
                {
                    idParam.Value = track.Id;
                    albumIdParam.Value = track.AlbumId;
                    artistIdParam.Value = track.ArtistId;
                    titleParam.Value = track.Title ?? "Unknown Title";
                    albumParam.Value = track.Album ?? "Unknown Album";
                    artistParam.Value = track.Artist ?? "Unknown Artist";
                    yearParam.Value = (object?)track.Year ?? DBNull.Value;
                    genreParam.Value = (object?)track.Genre ?? DBNull.Value;
                    bpmParam.Value = (object?)track.Bpm ?? DBNull.Value;
                    isFavoriteParam.Value = track.IsFavorite ? 1 : 0;
                    filePathParam.Value = track.FilePath;
                    fileSizeParam.Value = track.FileSize;
                    bitrateParam.Value = track.Bitrate;
                    durationParam.Value = (int)track.Duration.TotalSeconds;
                    dateAddedParam.Value = track.DateAdded.ToString("O");
                    lastModifiedParam.Value = track.LastModified;
                    isPresentParam.Value = track.IsPresent ? 1 : 0;

                    command.ExecuteNonQuery();
                }

                transaction.Commit();
            }
        }

        public Track? GetTrack(string id)
        {
            lock (_dbLock)
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
                        Bpm = reader.IsDBNull(reader.GetOrdinal("bpm")) ? null : reader.GetInt32(reader.GetOrdinal("bpm")),
                        IsFavorite = reader.GetBoolean(reader.GetOrdinal("is_favorite")),
                        FilePath = reader.GetString(reader.GetOrdinal("file_path")),
                        FileSize = reader.GetInt64(reader.GetOrdinal("file_size")),
                        Bitrate = reader.GetInt32(reader.GetOrdinal("bitrate")),
                        Duration = TimeSpan.FromSeconds(reader.GetInt64(reader.GetOrdinal("duration"))),
                        DateAdded = DateTime.Parse(reader.GetString(reader.GetOrdinal("date_added"))),
                        LastModified = reader.GetString(reader.GetOrdinal("last_modified")),
                        IsPresent = reader.GetBoolean(reader.GetOrdinal("is_present"))
                    };
                }
                return null;
            }
        }

        public Track? GetTrackByPath(string filePath)
        {
            lock (_dbLock)
            {
                using var command = new SqliteCommand("SELECT * FROM tracks WHERE file_path = @file_path", _connection);
                command.Parameters.AddWithValue("@file_path", filePath);

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
                        Bpm = reader.IsDBNull(reader.GetOrdinal("bpm")) ? null : reader.GetInt32(reader.GetOrdinal("bpm")),
                        IsFavorite = reader.GetBoolean(reader.GetOrdinal("is_favorite")),
                        FilePath = reader.GetString(reader.GetOrdinal("file_path")),
                        FileSize = reader.GetInt64(reader.GetOrdinal("file_size")),
                        Bitrate = reader.GetInt32(reader.GetOrdinal("bitrate")),
                        Duration = TimeSpan.FromSeconds(reader.GetInt64(reader.GetOrdinal("duration"))),
                        DateAdded = DateTime.Parse(reader.GetString(reader.GetOrdinal("date_added"))),
                        LastModified = reader.GetString(reader.GetOrdinal("last_modified")),
                        IsPresent = reader.GetBoolean(reader.GetOrdinal("is_present"))
                    };
                }
                return null;
            }
        }

        public List<Track> GetAllTracks()
        {
            lock (_dbLock)
            {
                var list = new List<Track>();
                using var command = new SqliteCommand("SELECT * FROM tracks", _connection);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new Track
                    {
                        Id = reader.GetString(reader.GetOrdinal("id")),
                        AlbumId = reader.GetString(reader.GetOrdinal("album_id")),
                        ArtistId = reader.GetString(reader.GetOrdinal("artist_id")),
                        Title = reader.GetString(reader.GetOrdinal("title")),
                        Album = reader.GetString(reader.GetOrdinal("album")),
                        Artist = reader.GetString(reader.GetOrdinal("artist")),
                        Year = reader.IsDBNull(reader.GetOrdinal("year")) ? null : reader.GetInt32(reader.GetOrdinal("year")),
                        Genre = reader.IsDBNull(reader.GetOrdinal("genre")) ? null : reader.GetString(reader.GetOrdinal("genre")),
                        Bpm = reader.IsDBNull(reader.GetOrdinal("bpm")) ? null : reader.GetInt32(reader.GetOrdinal("bpm")),
                        IsFavorite = reader.GetBoolean(reader.GetOrdinal("is_favorite")),
                        FilePath = reader.GetString(reader.GetOrdinal("file_path")),
                        FileSize = reader.GetInt64(reader.GetOrdinal("file_size")),
                        Bitrate = reader.GetInt32(reader.GetOrdinal("bitrate")),
                        Duration = TimeSpan.FromSeconds(reader.GetInt64(reader.GetOrdinal("duration"))),
                        DateAdded = DateTime.Parse(reader.GetString(reader.GetOrdinal("date_added"))),
                        LastModified = reader.GetString(reader.GetOrdinal("last_modified")),
                        IsPresent = reader.GetBoolean(reader.GetOrdinal("is_present"))
                    });
                }
                return list;
            }
        }

        public void UpsertAlbum(Album album)
        {
            lock (_dbLock)
            {
                using var command = new SqliteCommand(@"INSERT INTO albums 
                    (id, track_ids, artist_id, title, artist, year, genre, is_favorite, date_added) VALUES 
                    (@id, @track_ids, @artist_id, @title, @artist, @year, @genre, @is_favorite, @date_added)
                    ON CONFLICT(id) DO UPDATE SET
                        track_ids = excluded.track_ids,
                        year = excluded.year,
                        genre = excluded.genre;",
                _connection);

                string jsonTracks = JsonSerializer.Serialize(new List<string>(album.TrackIds), DbJsonContext.Default.ListString);

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
        }

        public void UpsertAlbumsBatch(IEnumerable<Album> albums)
        {
            lock (_dbLock)
            {
                using var transaction = _connection.BeginTransaction();
                using var command = new SqliteCommand(@"INSERT INTO albums 
                    (id, track_ids, artist_id, title, artist, year, genre, is_favorite, date_added) VALUES 
                    (@id, @track_ids, @artist_id, @title, @artist, @year, @genre, @is_favorite, @date_added)
                    ON CONFLICT(id) DO UPDATE SET
                        track_ids = excluded.track_ids,
                        year = excluded.year,
                        genre = excluded.genre;",
                _connection, transaction);

                var idParam = command.Parameters.Add("@id", SqliteType.Text);
                var trackIdsParam = command.Parameters.Add("@track_ids", SqliteType.Text);
                var artistIdParam = command.Parameters.Add("@artist_id", SqliteType.Text);
                var titleParam = command.Parameters.Add("@title", SqliteType.Text);
                var artistParam = command.Parameters.Add("@artist", SqliteType.Text);
                var yearParam = command.Parameters.Add("@year", SqliteType.Integer);
                var genreParam = command.Parameters.Add("@genre", SqliteType.Text);
                var isFavoriteParam = command.Parameters.Add("@is_favorite", SqliteType.Integer);
                var dateAddedParam = command.Parameters.Add("@date_added", SqliteType.Text);

                foreach (var album in albums)
                {
                    idParam.Value = album.Id;
                    trackIdsParam.Value = JsonSerializer.Serialize(new List<string>(album.TrackIds), DbJsonContext.Default.ListString);
                    artistIdParam.Value = album.ArtistId;
                    titleParam.Value = album.Title ?? "Unknown Album";
                    artistParam.Value = album.Artist ?? "Unknown Artist";
                    yearParam.Value = (object?)album.Year ?? DBNull.Value;
                    genreParam.Value = (object?)album.Genre ?? DBNull.Value;
                    isFavoriteParam.Value = album.IsFavorite ? 1 : 0;
                    dateAddedParam.Value = album.DateAdded.ToString("O");

                    command.ExecuteNonQuery();
                }

                transaction.Commit();
            }
        }

        public void UpsertArtist(Artist artist)
        {
            lock (_dbLock)
            {
                using var command = new SqliteCommand(@"INSERT INTO artists 
                    (id, album_ids, name, is_favorite, date_added) VALUES 
                    (@id, @album_ids, @name, @is_favorite, @date_added)
                    ON CONFLICT(id) DO UPDATE SET
                        album_ids = excluded.album_ids;",
                _connection);

                string jsonAlbums = JsonSerializer.Serialize(new List<string>(artist.AlbumIds), DbJsonContext.Default.ListString);

                command.Parameters.AddWithValue("@id", artist.Id);
                command.Parameters.AddWithValue("@album_ids", jsonAlbums);
                command.Parameters.AddWithValue("@name", artist.Name);
                command.Parameters.AddWithValue("@is_favorite", artist.IsFavorite ? 1 : 0);
                command.Parameters.AddWithValue("@date_added", artist.DateAdded.ToString("O"));

                command.ExecuteNonQuery();
            }
        }

        public void UpsertArtistsBatch(IEnumerable<Artist> artists)
        {
            lock (_dbLock)
            {
                using var transaction = _connection.BeginTransaction();
                using var command = new SqliteCommand(@"INSERT INTO artists 
                    (id, album_ids, name, is_favorite, date_added) VALUES 
                    (@id, @album_ids, @name, @is_favorite, @date_added)
                    ON CONFLICT(id) DO UPDATE SET
                        album_ids = excluded.album_ids;",
                _connection, transaction);

                var idParam = command.Parameters.Add("@id", SqliteType.Text);
                var albumIdsParam = command.Parameters.Add("@album_ids", SqliteType.Text);
                var nameParam = command.Parameters.Add("@name", SqliteType.Text);
                var isFavoriteParam = command.Parameters.Add("@is_favorite", SqliteType.Integer);
                var dateAddedParam = command.Parameters.Add("@date_added", SqliteType.Text);

                foreach (var artist in artists)
                {
                    idParam.Value = artist.Id;
                    albumIdsParam.Value = JsonSerializer.Serialize(new List<string>(artist.AlbumIds), DbJsonContext.Default.ListString);
                    nameParam.Value = artist.Name ?? "Unknown Artist";
                    isFavoriteParam.Value = artist.IsFavorite ? 1 : 0;
                    dateAddedParam.Value = artist.DateAdded.ToString("O");

                    command.ExecuteNonQuery();
                }

                transaction.Commit();
            }
        }

        public Album? GetAlbum(string id)
        {
            lock (_dbLock)
            {
                using var command = new SqliteCommand("SELECT * FROM albums WHERE id = @id", _connection);
                command.Parameters.AddWithValue("@id", id);
                using var reader = command.ExecuteReader();
                if (reader.Read())
                {
                    var jsonTracks = reader.GetString(reader.GetOrdinal("track_ids"));
                    var trackIds = JsonSerializer.Deserialize(jsonTracks, DbJsonContext.Default.ListString)?.ToArray() ?? Array.Empty<string>();
                    return new Album
                    {
                        Id = reader.GetString(reader.GetOrdinal("id")),
                        TrackIds = trackIds,
                        ArtistId = reader.GetString(reader.GetOrdinal("artist_id")),
                        Title = reader.GetString(reader.GetOrdinal("title")),
                        Artist = reader.GetString(reader.GetOrdinal("artist")),
                        Year = reader.IsDBNull(reader.GetOrdinal("year")) ? null : reader.GetInt32(reader.GetOrdinal("year")),
                        Genre = reader.IsDBNull(reader.GetOrdinal("genre")) ? null : reader.GetString(reader.GetOrdinal("genre")),
                        IsFavorite = reader.GetBoolean(reader.GetOrdinal("is_favorite")),
                        DateAdded = DateTime.Parse(reader.GetString(reader.GetOrdinal("date_added")))
                    };
                }
                return null;
            }
        }

        public List<Album> GetAllAlbums()
        {
            lock (_dbLock)
            {
                var list = new List<Album>();
                using var command = new SqliteCommand("SELECT * FROM albums", _connection);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var jsonTracks = reader.GetString(reader.GetOrdinal("track_ids"));
                    var trackIds = JsonSerializer.Deserialize(jsonTracks, DbJsonContext.Default.ListString)?.ToArray() ?? Array.Empty<string>();
                    list.Add(new Album
                    {
                        Id = reader.GetString(reader.GetOrdinal("id")),
                        TrackIds = trackIds,
                        ArtistId = reader.GetString(reader.GetOrdinal("artist_id")),
                        Title = reader.GetString(reader.GetOrdinal("title")),
                        Artist = reader.GetString(reader.GetOrdinal("artist")),
                        Year = reader.IsDBNull(reader.GetOrdinal("year")) ? null : reader.GetInt32(reader.GetOrdinal("year")),
                        Genre = reader.IsDBNull(reader.GetOrdinal("genre")) ? null : reader.GetString(reader.GetOrdinal("genre")),
                        IsFavorite = reader.GetBoolean(reader.GetOrdinal("is_favorite")),
                        DateAdded = DateTime.Parse(reader.GetString(reader.GetOrdinal("date_added")))
                    });
                }
                return list;
            }
        }

        public Artist? GetArtist(string id)
        {
            lock (_dbLock)
            {
                using var command = new SqliteCommand("SELECT * FROM artists WHERE id = @id", _connection);
                command.Parameters.AddWithValue("@id", id);
                using var reader = command.ExecuteReader();
                if (reader.Read())
                {
                    var jsonAlbums = reader.GetString(reader.GetOrdinal("album_ids"));
                    var albumIds = JsonSerializer.Deserialize(jsonAlbums, DbJsonContext.Default.ListString)?.ToArray() ?? Array.Empty<string>();
                    return new Artist
                    {
                        Id = reader.GetString(reader.GetOrdinal("id")),
                        AlbumIds = albumIds,
                        Name = reader.GetString(reader.GetOrdinal("name")),
                        IsFavorite = reader.GetBoolean(reader.GetOrdinal("is_favorite")),
                        DateAdded = DateTime.Parse(reader.GetString(reader.GetOrdinal("date_added")))
                    };
                }
                return null;
            }
        }

        public List<Artist> GetAllArtists()
        {
            lock (_dbLock)
            {
                var list = new List<Artist>();
                using var command = new SqliteCommand("SELECT * FROM artists", _connection);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var jsonAlbums = reader.GetString(reader.GetOrdinal("album_ids"));
                    var albumIds = JsonSerializer.Deserialize(jsonAlbums, DbJsonContext.Default.ListString)?.ToArray() ?? Array.Empty<string>();
                    list.Add(new Artist
                    {
                        Id = reader.GetString(reader.GetOrdinal("id")),
                        AlbumIds = albumIds,
                        Name = reader.GetString(reader.GetOrdinal("name")),
                        IsFavorite = reader.GetBoolean(reader.GetOrdinal("is_favorite")),
                        DateAdded = DateTime.Parse(reader.GetString(reader.GetOrdinal("date_added")))
                    });
                }
                return list;
            }
        }

        public void Dispose()
        {
            lock (_dbLock)
            {
                _connection?.Dispose();
            }
        }
    }

    public static class IdGenerator
    {
        public static string NewTrackId() => $"trk_{Guid.NewGuid():N}";
        
        public static string GetArtistId(string artistName)
        {
            return $"art_{HashString(artistName.Trim().ToLowerInvariant())}";
        }

        public static string GetAlbumId(string albumTitle, string artistName)
        {
            return $"alb_{HashString((artistName.Trim() + "_" + albumTitle.Trim()).ToLowerInvariant())}";
        }

        private static string HashString(string input)
        {
            using var md5 = System.Security.Cryptography.MD5.Create();
            var hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
            var sb = new StringBuilder();
            foreach (var b in hashBytes)
            {
                sb.Append(b.ToString("x2"));
            }
            return sb.ToString();
        }
    }

    [JsonSerializable(typeof(List<string>))]
    internal partial class DbJsonContext : JsonSerializerContext { }
}
