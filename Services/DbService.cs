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
                    track_number  INTEGER,
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

                using var lyricTableCommand = new SqliteCommand(@"CREATE TABLE IF NOT EXISTS lyrics (
                    track_id    TEXT PRIMARY KEY,
                    lyrics      TEXT
                );", _connection);
                lyricTableCommand.ExecuteNonQuery();

                using var playlistTableCommand = new SqliteCommand(@"CREATE TABLE IF NOT EXISTS playlists (
                    id        TEXT PRIMARY KEY,
                    title     TEXT NOT NULL,
                    track_ids TEXT NOT NULL
                );", _connection);
                playlistTableCommand.ExecuteNonQuery();
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
                    DROP TABLE IF EXISTS lyrics;
                    DROP TABLE IF EXISTS playlists;
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
                    (id, album_id, artist_id, title, album, artist, track_number, year, genre, bpm, is_favorite, file_path, file_size, bitrate, duration, date_added, last_modified, is_present) VALUES 
                    (@id, @album_id, @artist_id, @title, @album, @artist, @track_number, @year, @genre, @bpm, @is_favorite, @file_path, @file_size, @bitrate, @duration, @date_added, @last_modified, @is_present);",
                _connection);

                command.Parameters.AddWithValue("@id", track.Id);
                command.Parameters.AddWithValue("@album_id", track.AlbumId);
                command.Parameters.AddWithValue("@artist_id", track.ArtistId);
                command.Parameters.AddWithValue("@title", track.Title);
                command.Parameters.AddWithValue("@album", track.Album);
                command.Parameters.AddWithValue("@artist", track.Artist);
                command.Parameters.AddWithValue("@track_number", (object?)track.TrackNumber ?? DBNull.Value);
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

        public void SetTrackFavorite(string id, bool isFavorite)
        {
            lock (_dbLock)
            {
                using var command = new SqliteCommand("UPDATE tracks SET is_favorite = @is_favorite WHERE id = @id;", _connection);
                command.Parameters.AddWithValue("@id", id);
                command.Parameters.AddWithValue("@is_favorite", isFavorite ? 1 : 0);
                command.ExecuteNonQuery();
            }
        }

        public void UpsertTracksBatch(IEnumerable<Track> tracks)
        {
            lock (_dbLock)
            {
                using var transaction = _connection.BeginTransaction();
                using var command = new SqliteCommand(@"INSERT OR REPLACE INTO tracks 
            (id, album_id, artist_id, title, album, artist, track_number, year, genre, bpm, is_favorite, file_path, file_size, bitrate, duration, date_added, last_modified, is_present) VALUES 
            (@id, @album_id, @artist_id, @title, @album, @artist, @track_number, @year, @genre, @bpm, @is_favorite, @file_path, @file_size, @bitrate, @duration, @date_added, @last_modified, @is_present);",
                _connection, transaction);

                var idParam = command.Parameters.Add("@id", SqliteType.Text);
                var albumIdParam = command.Parameters.Add("@album_id", SqliteType.Text);
                var artistIdParam = command.Parameters.Add("@artist_id", SqliteType.Text);
                var titleParam = command.Parameters.Add("@title", SqliteType.Text);
                var albumParam = command.Parameters.Add("@album", SqliteType.Text);
                var artistParam = command.Parameters.Add("@artist", SqliteType.Text);
                var trackNumberParam = command.Parameters.Add("@track_number", SqliteType.Integer);
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
                    trackNumberParam.Value = (object?)track.TrackNumber ?? DBNull.Value;
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

        public void UpsertLyricsBatch(IEnumerable<(string TrackId, string Lyrics)> lyrics)
        {
            lock (_dbLock)
            {
                using var transaction = _connection.BeginTransaction();
                using var command = new SqliteCommand(@"INSERT OR REPLACE INTO lyrics 
            (track_id, lyrics) VALUES 
            (@track_id, @lyrics);",
                _connection, transaction);

                var idParam = command.Parameters.Add("@track_id", SqliteType.Text);
                var lyricsParam = command.Parameters.Add("@lyrics", SqliteType.Text);

                foreach (var lyric in lyrics)
                {
                    idParam.Value = lyric.TrackId;
                    lyricsParam.Value = lyric.Lyrics;

                    command.ExecuteNonQuery();
                }

                transaction.Commit();
            }
        }

        public string? GetLyrics(string trackId)
        {
            using var connection = new SqliteConnection("Data Source=" + DbPath);
            connection.Open();
            using var command = new SqliteCommand("SELECT lyrics FROM lyrics WHERE track_id = @track_id", connection);
            command.Parameters.AddWithValue("@track_id", trackId);
            return command.ExecuteScalar() as string;
        }

        public Track? GetTrack(string id)
        {
            using var connection = new SqliteConnection("Data Source=" + DbPath);
            connection.Open();
            using var command = new SqliteCommand("SELECT * FROM tracks WHERE id = @id", connection);
            command.Parameters.AddWithValue("@id", id);
            using var reader = command.ExecuteReader();
            var idOrd = reader.GetOrdinal("id");
            var albumIdOrd = reader.GetOrdinal("album_id");
            var artistIdOrd = reader.GetOrdinal("artist_id");
            var titleOrd = reader.GetOrdinal("title");
            var albumOrd = reader.GetOrdinal("album");
            var artistOrd = reader.GetOrdinal("artist");
            var trackNumberOrd = reader.GetOrdinal("track_number");
            var yearOrd = reader.GetOrdinal("year");
            var genreOrd = reader.GetOrdinal("genre");
            var bpmOrd = reader.GetOrdinal("bpm");
            var isFavoriteOrd = reader.GetOrdinal("is_favorite");
            var filePathOrd = reader.GetOrdinal("file_path");
            var fileSizeOrd = reader.GetOrdinal("file_size");
            var bitrateOrd = reader.GetOrdinal("bitrate");
            var durationOrd = reader.GetOrdinal("duration");
            var dateAddedOrd = reader.GetOrdinal("date_added");
            var lastModifiedOrd = reader.GetOrdinal("last_modified");
            var isPresentOrd = reader.GetOrdinal("is_present");
            if (reader.Read())
            {
                return new Track
                {
                    Id = reader.GetString(idOrd),
                    AlbumId = reader.GetString(albumIdOrd),
                    ArtistId = reader.GetString(artistIdOrd),
                    Title = reader.GetString(titleOrd),
                    Album = reader.GetString(albumOrd),
                    Artist = reader.GetString(artistOrd),
                    TrackNumber = reader.IsDBNull(trackNumberOrd) ? null : reader.GetInt32(trackNumberOrd),
                    Year = reader.IsDBNull(yearOrd) ? null : reader.GetInt32(yearOrd),
                    Genre = reader.IsDBNull(genreOrd) ? null : reader.GetString(genreOrd),
                    Bpm = reader.IsDBNull(bpmOrd) ? null : reader.GetInt32(bpmOrd),
                    IsFavorite = reader.GetBoolean(isFavoriteOrd),
                    FilePath = reader.GetString(filePathOrd),
                    FileSize = reader.GetInt64(fileSizeOrd),
                    Bitrate = reader.GetInt32(bitrateOrd),
                    Duration = TimeSpan.FromSeconds(reader.GetInt64(durationOrd)),
                    DateAdded = DateTime.Parse(reader.GetString(dateAddedOrd)),
                    LastModified = reader.GetString(lastModifiedOrd),
                    IsPresent = reader.GetBoolean(isPresentOrd)
                };
            }
            return null;
        }

        public IEnumerable<Track> GetTracksByIds(IEnumerable<string> ids)
        {
            var idList = ids.ToList();
            if (idList.Count == 0) yield break;

            var paramNames = idList.Select((_, i) => $"@id{i}");
            var sql = $"SELECT * FROM tracks WHERE id IN ({string.Join(", ", paramNames)})";

            using var connection = new SqliteConnection("Data Source=" + DbPath);
            connection.Open();
            using var command = new SqliteCommand(sql, connection);
            for (int i = 0; i < idList.Count; i++)
                command.Parameters.AddWithValue($"@id{i}", idList[i]);

            using var reader = command.ExecuteReader();
            var idOrd = reader.GetOrdinal("id");
            var albumIdOrd = reader.GetOrdinal("album_id");
            var artistIdOrd = reader.GetOrdinal("artist_id");
            var titleOrd = reader.GetOrdinal("title");
            var albumOrd = reader.GetOrdinal("album");
            var artistOrd = reader.GetOrdinal("artist");
            var trackNumberOrd = reader.GetOrdinal("track_number");
            var yearOrd = reader.GetOrdinal("year");
            var genreOrd = reader.GetOrdinal("genre");
            var bpmOrd = reader.GetOrdinal("bpm");
            var isFavoriteOrd = reader.GetOrdinal("is_favorite");
            var filePathOrd = reader.GetOrdinal("file_path");
            var fileSizeOrd = reader.GetOrdinal("file_size");
            var bitrateOrd = reader.GetOrdinal("bitrate");
            var durationOrd = reader.GetOrdinal("duration");
            var dateAddedOrd = reader.GetOrdinal("date_added");
            var lastModifiedOrd = reader.GetOrdinal("last_modified");
            var isPresentOrd = reader.GetOrdinal("is_present");

            var fetched = new Dictionary<string, Track>(idList.Count);
            while (reader.Read())
            {
                var track = new Track
                {
                    Id = reader.GetString(idOrd),
                    AlbumId = reader.GetString(albumIdOrd),
                    ArtistId = reader.GetString(artistIdOrd),
                    Title = reader.GetString(titleOrd),
                    Album = reader.GetString(albumOrd),
                    Artist = reader.GetString(artistOrd),
                    TrackNumber = reader.IsDBNull(trackNumberOrd) ? null : reader.GetInt32(trackNumberOrd),
                    Year = reader.IsDBNull(yearOrd) ? null : reader.GetInt32(yearOrd),
                    Genre = reader.IsDBNull(genreOrd) ? null : reader.GetString(genreOrd),
                    Bpm = reader.IsDBNull(bpmOrd) ? null : reader.GetInt32(bpmOrd),
                    IsFavorite = reader.GetBoolean(isFavoriteOrd),
                    FilePath = reader.GetString(filePathOrd),
                    FileSize = reader.GetInt64(fileSizeOrd),
                    Bitrate = reader.GetInt32(bitrateOrd),
                    Duration = TimeSpan.FromSeconds(reader.GetInt64(durationOrd)),
                    DateAdded = DateTime.Parse(reader.GetString(dateAddedOrd)),
                    LastModified = reader.GetString(lastModifiedOrd),
                    IsPresent = reader.GetBoolean(isPresentOrd)
                };
                fetched[track.Id] = track;
            }

            foreach (var id in idList)
                if (fetched.TryGetValue(id, out var t)) yield return t;
        }

        public Track? GetTrackByPath(string filePath)
        {
            using var connection = new SqliteConnection("Data Source=" + DbPath);
            connection.Open();
            using var command = new SqliteCommand("SELECT * FROM tracks WHERE file_path = @file_path", connection);
            command.Parameters.AddWithValue("@file_path", filePath);
            using var reader = command.ExecuteReader();
            var idOrd = reader.GetOrdinal("id");
            var albumIdOrd = reader.GetOrdinal("album_id");
            var artistIdOrd = reader.GetOrdinal("artist_id");
            var titleOrd = reader.GetOrdinal("title");
            var albumOrd = reader.GetOrdinal("album");
            var artistOrd = reader.GetOrdinal("artist");
            var trackNumberOrd = reader.GetOrdinal("track_number");
            var yearOrd = reader.GetOrdinal("year");
            var genreOrd = reader.GetOrdinal("genre");
            var bpmOrd = reader.GetOrdinal("bpm");
            var isFavoriteOrd = reader.GetOrdinal("is_favorite");
            var filePathOrd = reader.GetOrdinal("file_path");
            var fileSizeOrd = reader.GetOrdinal("file_size");
            var bitrateOrd = reader.GetOrdinal("bitrate");
            var durationOrd = reader.GetOrdinal("duration");
            var dateAddedOrd = reader.GetOrdinal("date_added");
            var lastModifiedOrd = reader.GetOrdinal("last_modified");
            var isPresentOrd = reader.GetOrdinal("is_present");
            if (reader.Read())
            {
                return new Track
                {
                    Id = reader.GetString(idOrd),
                    AlbumId = reader.GetString(albumIdOrd),
                    ArtistId = reader.GetString(artistIdOrd),
                    Title = reader.GetString(titleOrd),
                    Album = reader.GetString(albumOrd),
                    Artist = reader.GetString(artistOrd),
                    TrackNumber = reader.IsDBNull(trackNumberOrd) ? null : reader.GetInt32(trackNumberOrd),
                    Year = reader.IsDBNull(yearOrd) ? null : reader.GetInt32(yearOrd),
                    Genre = reader.IsDBNull(genreOrd) ? null : reader.GetString(genreOrd),
                    Bpm = reader.IsDBNull(bpmOrd) ? null : reader.GetInt32(bpmOrd),
                    IsFavorite = reader.GetBoolean(isFavoriteOrd),
                    FilePath = reader.GetString(filePathOrd),
                    FileSize = reader.GetInt64(fileSizeOrd),
                    Bitrate = reader.GetInt32(bitrateOrd),
                    Duration = TimeSpan.FromSeconds(reader.GetInt64(durationOrd)),
                    DateAdded = DateTime.Parse(reader.GetString(dateAddedOrd)),
                    LastModified = reader.GetString(lastModifiedOrd),
                    IsPresent = reader.GetBoolean(isPresentOrd)
                };
            }
            return null;
        }

        public IEnumerable<Track> GetAllTracks()
        {
            using var connection = new SqliteConnection("Data Source=" + DbPath);
            connection.Open();
            using var command = new SqliteCommand("SELECT * FROM tracks", connection);
            using var reader = command.ExecuteReader();
            var idOrd = reader.GetOrdinal("id");
            var albumIdOrd = reader.GetOrdinal("album_id");
            var artistIdOrd = reader.GetOrdinal("artist_id");
            var titleOrd = reader.GetOrdinal("title");
            var albumOrd = reader.GetOrdinal("album");
            var artistOrd = reader.GetOrdinal("artist");
            var trackNumberOrd = reader.GetOrdinal("track_number");
            var yearOrd = reader.GetOrdinal("year");
            var genreOrd = reader.GetOrdinal("genre");
            var bpmOrd = reader.GetOrdinal("bpm");
            var isFavoriteOrd = reader.GetOrdinal("is_favorite");
            var filePathOrd = reader.GetOrdinal("file_path");
            var fileSizeOrd = reader.GetOrdinal("file_size");
            var bitrateOrd = reader.GetOrdinal("bitrate");
            var durationOrd = reader.GetOrdinal("duration");
            var dateAddedOrd = reader.GetOrdinal("date_added");
            var lastModifiedOrd = reader.GetOrdinal("last_modified");
            var isPresentOrd = reader.GetOrdinal("is_present");
            while (reader.Read())
            {
                yield return new Track
                {
                    Id = reader.GetString(idOrd),
                    AlbumId = reader.GetString(albumIdOrd),
                    ArtistId = reader.GetString(artistIdOrd),
                    Title = reader.GetString(titleOrd),
                    Album = reader.GetString(albumOrd),
                    Artist = reader.GetString(artistOrd),
                    TrackNumber = reader.IsDBNull(trackNumberOrd) ? null : reader.GetInt32(trackNumberOrd),
                    Year = reader.IsDBNull(yearOrd) ? null : reader.GetInt32(yearOrd),
                    Genre = reader.IsDBNull(genreOrd) ? null : reader.GetString(genreOrd),
                    Bpm = reader.IsDBNull(bpmOrd) ? null : reader.GetInt32(bpmOrd),
                    IsFavorite = reader.GetBoolean(isFavoriteOrd),
                    FilePath = reader.GetString(filePathOrd),
                    FileSize = reader.GetInt64(fileSizeOrd),
                    Bitrate = reader.GetInt32(bitrateOrd),
                    Duration = TimeSpan.FromSeconds(reader.GetInt64(durationOrd)),
                    DateAdded = DateTime.Parse(reader.GetString(dateAddedOrd)),
                    LastModified = reader.GetString(lastModifiedOrd),
                    IsPresent = reader.GetBoolean(isPresentOrd)
                };
            }
        }

        public IEnumerable<Track> GetAllFavoriteTracks()
        {
            using var connection = new SqliteConnection("Data Source=" + DbPath);
            connection.Open();
            using var command = new SqliteCommand("SELECT * FROM tracks WHERE is_favorite", connection);
            using var reader = command.ExecuteReader();
            var idOrd = reader.GetOrdinal("id");
            var albumIdOrd = reader.GetOrdinal("album_id");
            var artistIdOrd = reader.GetOrdinal("artist_id");
            var titleOrd = reader.GetOrdinal("title");
            var albumOrd = reader.GetOrdinal("album");
            var artistOrd = reader.GetOrdinal("artist");
            var trackNumberOrd = reader.GetOrdinal("track_number");
            var yearOrd = reader.GetOrdinal("year");
            var genreOrd = reader.GetOrdinal("genre");
            var bpmOrd = reader.GetOrdinal("bpm");
            var isFavoriteOrd = reader.GetOrdinal("is_favorite");
            var filePathOrd = reader.GetOrdinal("file_path");
            var fileSizeOrd = reader.GetOrdinal("file_size");
            var bitrateOrd = reader.GetOrdinal("bitrate");
            var durationOrd = reader.GetOrdinal("duration");
            var dateAddedOrd = reader.GetOrdinal("date_added");
            var lastModifiedOrd = reader.GetOrdinal("last_modified");
            var isPresentOrd = reader.GetOrdinal("is_present");
            while (reader.Read())
            {
                yield return new Track
                {
                    Id = reader.GetString(idOrd),
                    AlbumId = reader.GetString(albumIdOrd),
                    ArtistId = reader.GetString(artistIdOrd),
                    Title = reader.GetString(titleOrd),
                    Album = reader.GetString(albumOrd),
                    Artist = reader.GetString(artistOrd),
                    TrackNumber = reader.IsDBNull(trackNumberOrd) ? null : reader.GetInt32(trackNumberOrd),
                    Year = reader.IsDBNull(yearOrd) ? null : reader.GetInt32(yearOrd),
                    Genre = reader.IsDBNull(genreOrd) ? null : reader.GetString(genreOrd),
                    Bpm = reader.IsDBNull(bpmOrd) ? null : reader.GetInt32(bpmOrd),
                    IsFavorite = reader.GetBoolean(isFavoriteOrd),
                    FilePath = reader.GetString(filePathOrd),
                    FileSize = reader.GetInt64(fileSizeOrd),
                    Bitrate = reader.GetInt32(bitrateOrd),
                    Duration = TimeSpan.FromSeconds(reader.GetInt64(durationOrd)),
                    DateAdded = DateTime.Parse(reader.GetString(dateAddedOrd)),
                    LastModified = reader.GetString(lastModifiedOrd),
                    IsPresent = reader.GetBoolean(isPresentOrd)
                };
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
            using var connection = new SqliteConnection("Data Source=" + DbPath);
            connection.Open();
            using var command = new SqliteCommand("SELECT * FROM albums WHERE id = @id", connection);
            command.Parameters.AddWithValue("@id", id);
            using var reader = command.ExecuteReader();
            var idOrd = reader.GetOrdinal("id");
            var trackIdsOrd = reader.GetOrdinal("track_ids");
            var artistIdOrd = reader.GetOrdinal("artist_id");
            var titleOrd = reader.GetOrdinal("title");
            var artistOrd = reader.GetOrdinal("artist");
            var yearOrd = reader.GetOrdinal("year");
            var genreOrd = reader.GetOrdinal("genre");
            var isFavoriteOrd = reader.GetOrdinal("is_favorite");
            var dateAddedOrd = reader.GetOrdinal("date_added");
            if (reader.Read())
            {
                var jsonTracks = reader.GetString(trackIdsOrd);
                var trackIds = JsonSerializer.Deserialize(jsonTracks, DbJsonContext.Default.ListString)?.ToArray() ?? Array.Empty<string>();
                return new Album
                {
                    Id = reader.GetString(idOrd),
                    TrackIds = trackIds,
                    ArtistId = reader.GetString(artistIdOrd),
                    Title = reader.GetString(titleOrd),
                    Artist = reader.GetString(artistOrd),
                    Year = reader.IsDBNull(yearOrd) ? null : reader.GetInt32(yearOrd),
                    Genre = reader.IsDBNull(genreOrd) ? null : reader.GetString(genreOrd),
                    IsFavorite = reader.GetBoolean(isFavoriteOrd),
                    DateAdded = DateTime.Parse(reader.GetString(dateAddedOrd))
                };
            }
            return null;
        }

        public IEnumerable<Album> GetAllAlbums()
        {
            using var connection = new SqliteConnection("Data Source=" + DbPath);
            connection.Open();
            using var command = new SqliteCommand("SELECT * FROM albums", connection);
            using var reader = command.ExecuteReader();
            var idOrd = reader.GetOrdinal("id");
            var trackIdsOrd = reader.GetOrdinal("track_ids");
            var artistIdOrd = reader.GetOrdinal("artist_id");
            var titleOrd = reader.GetOrdinal("title");
            var artistOrd = reader.GetOrdinal("artist");
            var yearOrd = reader.GetOrdinal("year");
            var genreOrd = reader.GetOrdinal("genre");
            var isFavoriteOrd = reader.GetOrdinal("is_favorite");
            var dateAddedOrd = reader.GetOrdinal("date_added");
            while (reader.Read())
            {
                var jsonTracks = reader.GetString(trackIdsOrd);
                var trackIds = JsonSerializer.Deserialize(jsonTracks, DbJsonContext.Default.ListString)?.ToArray() ?? Array.Empty<string>();
                yield return new Album
                {
                    Id = reader.GetString(idOrd),
                    TrackIds = trackIds,
                    ArtistId = reader.GetString(artistIdOrd),
                    Title = reader.GetString(titleOrd),
                    Artist = reader.GetString(artistOrd),
                    Year = reader.IsDBNull(yearOrd) ? null : reader.GetInt32(yearOrd),
                    Genre = reader.IsDBNull(genreOrd) ? null : reader.GetString(genreOrd),
                    IsFavorite = reader.GetBoolean(isFavoriteOrd),
                    DateAdded = DateTime.Parse(reader.GetString(dateAddedOrd))
                };
            }
        }

        public Artist? GetArtist(string id)
        {
            using var connection = new SqliteConnection("Data Source=" + DbPath);
            connection.Open();
            using var command = new SqliteCommand("SELECT * FROM artists WHERE id = @id", connection);
            command.Parameters.AddWithValue("@id", id);
            using var reader = command.ExecuteReader();
            var idOrd = reader.GetOrdinal("id");
            var albumIdsOrd = reader.GetOrdinal("album_ids");
            var nameOrd = reader.GetOrdinal("name");
            var isFavoriteOrd = reader.GetOrdinal("is_favorite");
            var dateAddedOrd = reader.GetOrdinal("date_added");
            if (reader.Read())
            {
                var jsonAlbums = reader.GetString(albumIdsOrd);
                var albumIds = JsonSerializer.Deserialize(jsonAlbums, DbJsonContext.Default.ListString)?.ToArray() ?? Array.Empty<string>();
                return new Artist
                {
                    Id = reader.GetString(idOrd),
                    AlbumIds = albumIds,
                    Name = reader.GetString(nameOrd),
                    IsFavorite = reader.GetBoolean(isFavoriteOrd),
                    DateAdded = DateTime.Parse(reader.GetString(dateAddedOrd))
                };
            }
            return null;
        }

        public IEnumerable<Artist> GetAllArtists()
        {
            using var connection = new SqliteConnection("Data Source=" + DbPath);
            connection.Open();
            using var command = new SqliteCommand("SELECT * FROM artists", connection);
            using var reader = command.ExecuteReader();
            var idOrd = reader.GetOrdinal("id");
            var albumIdsOrd = reader.GetOrdinal("album_ids");
            var nameOrd = reader.GetOrdinal("name");
            var isFavoriteOrd = reader.GetOrdinal("is_favorite");
            var dateAddedOrd = reader.GetOrdinal("date_added");
            while (reader.Read())
            {
                var jsonAlbums = reader.GetString(albumIdsOrd);
                var albumIds = JsonSerializer.Deserialize(jsonAlbums, DbJsonContext.Default.ListString)?.ToArray() ?? Array.Empty<string>();
                yield return new Artist
                {
                    Id = reader.GetString(idOrd),
                    AlbumIds = albumIds,
                    Name = reader.GetString(nameOrd),
                    IsFavorite = reader.GetBoolean(isFavoriteOrd),
                    DateAdded = DateTime.Parse(reader.GetString(dateAddedOrd))
                };
            }
        }

        public void UpsertPlaylist(Playlist playlist)
        {
            lock (_dbLock)
            {
                using var command = new SqliteCommand(@"INSERT INTO playlists 
                    (id, title, track_ids) VALUES 
                    (@id, @title, @track_ids)
                    ON CONFLICT(id) DO UPDATE SET
                        title = excluded.title,
                        track_ids = excluded.track_ids;",
                _connection);

                string jsonTracks = JsonSerializer.Serialize(new List<string>(playlist.TrackIds), DbJsonContext.Default.ListString);

                command.Parameters.AddWithValue("@id", playlist.Id);
                command.Parameters.AddWithValue("@title", playlist.Title);
                command.Parameters.AddWithValue("@track_ids", jsonTracks);

                command.ExecuteNonQuery();
            }
        }

        public void UpsertPlaylistsBatch(IEnumerable<Playlist> playlists)
        {
            lock (_dbLock)
            {
                using var transaction = _connection.BeginTransaction();
                using var command = new SqliteCommand(@"INSERT INTO playlists 
                    (id, title, track_ids) VALUES 
                    (@id, @title, @track_ids)
                    ON CONFLICT(id) DO UPDATE SET
                        title = excluded.title,
                        track_ids = excluded.track_ids;",
                _connection, transaction);

                var idParam = command.Parameters.Add("@id", SqliteType.Text);
                var titleParam = command.Parameters.Add("@title", SqliteType.Text);
                var trackIdsParam = command.Parameters.Add("@track_ids", SqliteType.Text);

                foreach (var playlist in playlists)
                {
                    idParam.Value = playlist.Id;
                    titleParam.Value = playlist.Title ?? "Unknown Playlist";
                    trackIdsParam.Value = JsonSerializer.Serialize(new List<string>(playlist.TrackIds), DbJsonContext.Default.ListString);

                    command.ExecuteNonQuery();
                }

                transaction.Commit();
            }
        }

        public Playlist? GetPlaylistFromId(string id)
        {
            using var connection = new SqliteConnection("Data Source=" + DbPath);
            connection.Open();
            using var command = new SqliteCommand("SELECT * FROM playlists WHERE id = @id", connection);
            command.Parameters.AddWithValue("@id", id);
            using var reader = command.ExecuteReader();
            var idOrd = reader.GetOrdinal("id");
            var titleOrd = reader.GetOrdinal("title");
            var trackIdsOrd = reader.GetOrdinal("track_ids");
            if (reader.Read())
            {
                var jsonTracks = reader.GetString(trackIdsOrd);
                var trackIds = JsonSerializer.Deserialize(jsonTracks, DbJsonContext.Default.ListString)?.ToArray() ?? Array.Empty<string>();
                return new Playlist
                {
                    Id = reader.GetString(idOrd),
                    Title = reader.GetString(titleOrd),
                    TrackIds = trackIds
                };
            }
            return null;
        }

        public IEnumerable<Playlist> GetAllPlaylists()
        {
            using var connection = new SqliteConnection("Data Source=" + DbPath);
            connection.Open();
            using var command = new SqliteCommand("SELECT * FROM playlists", connection);
            using var reader = command.ExecuteReader();
            var idOrd = reader.GetOrdinal("id");
            var titleOrd = reader.GetOrdinal("title");
            var trackIdsOrd = reader.GetOrdinal("track_ids");
            while (reader.Read())
            {
                var jsonTracks = reader.GetString(trackIdsOrd);
                var trackIds = JsonSerializer.Deserialize(jsonTracks, DbJsonContext.Default.ListString)?.ToArray() ?? Array.Empty<string>();
                yield return new Playlist
                {
                    Id = reader.GetString(idOrd),
                    Title = reader.GetString(titleOrd),
                    TrackIds = trackIds
                };
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

        public static string GetPlaylistId(string title, string relativePath)
        {
            return $"pls_{HashString((title.Trim() + "_" + relativePath.Trim()).ToLowerInvariant())}";
        }

        private static string HashString(string input)
        {
            var hashBytes = System.Security.Cryptography.MD5.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }
    }

    [JsonSerializable(typeof(List<string>))]
    internal partial class DbJsonContext : JsonSerializerContext { }
}
