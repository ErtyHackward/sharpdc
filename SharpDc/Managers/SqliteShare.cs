using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text;
using SharpDc.Hash;
using SharpDc.Helpers;
using SharpDc.Interfaces;
using SharpDc.Messages;
using SharpDc.Structs;

namespace SharpDc.Managers
{
    /// <summary>
    /// Provides sqlite share
    /// </summary>
    public class SqliteShare : IShare
    {
        private readonly object _syncRoot = new object();

        private readonly Queue<FileTask> _filesToAdd = new Queue<FileTask>();
        private readonly List<string> _excludeDirectories = new List<string>(); 
        
        private SQLiteConnection _connection;
        private SQLiteCommand _addFileCommand;
        private SQLiteCommand _searchFileByTthCommand;
        private SQLiteCommand _searchFileByNameCommand;
        private SQLiteCommand _deleteFileByTthCommand;
        private SQLiteCommand _deleteFileByIdCommand;
        private SQLiteCommand _readFilesCommand;
        private SQLiteCommand _readOldestFilesCommand;
        private SQLiteCommand _addDirectoryCommand;
        private SQLiteCommand _deleteDirectoryCommand;
        private SQLiteCommand _addExcludeDirectoryCommand;
        private SQLiteCommand _deleteExcludeDirectoryCommand;
        private SQLiteCommand _readFilesShortCommand;
        private SQLiteCommand _readDirectoriesCommand;
        private SQLiteCommand _deleteFilesByFolderCommand;
        private long _totalShared;

        public long TotalShared
        {
            get { return _totalShared; }
            private set {
                if (_totalShared != value)
                {
                    _totalShared = value;
                    OnTotalSharedChanged();
                }
            }
        }

        public int TotalFiles { get; private set; }
        public string DatabasePath { get; private set; }

        public event EventHandler TotalSharedChanged;

        protected virtual void OnTotalSharedChanged()
        {
            var handler = TotalSharedChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        public event EventHandler<SQLiteExceptionEventArgs> SqlError;

        protected virtual void OnSqlError(SQLiteExceptionEventArgs e)
        {
            var handler = SqlError;
            if (handler != null) handler(this, e);
        }

        public SqliteShare(string dbPath)
        {
            DatabasePath = dbPath;
            GetConnection();
            UpdateStats();
        }

        private void UpdateStats()
        {
            var connection = GetConnection();

            lock (_syncRoot)
            {
                var command = connection.CreateCommand();
                command.CommandText = "SELECT count(*) FROM share";
                TotalFiles = (int)command.ExecuteScalar();

                command.CommandText = "SELECT sum(size) FROM share";
                TotalShared = (long)command.ExecuteScalar();
            }
        }

        private void CreateCommands()
        {
            _addFileCommand = new SQLiteCommand("INSERT INTO share (tth,  filename,  size,  virtual,  systempath,  createtime, lastwritetime) " +
                                               "values (@tth, @filename, @size, @virtual, @systemPath, datetime('now','localtime'), @lastWrite) ", _connection);
            _addFileCommand.Parameters.Add("@tth", DbType.StringFixedLength);
            _addFileCommand.Parameters.Add("@filename", DbType.String);
            _addFileCommand.Parameters.Add("@size", DbType.Int64);
            _addFileCommand.Parameters.Add("@virtual", DbType.String);
            _addFileCommand.Parameters.Add("@systemPath", DbType.String);
            _addFileCommand.Parameters.Add("@lastWrite", DbType.DateTime);

            const string allRows = "tth, filename, size, virtual, systempath, createtime, lastwritetime, lastaccess, uploaded";

            _searchFileByTthCommand = new SQLiteCommand(string.Format("SELECT {0} FROM share WHERE tth = @tth", allRows), _connection);
            _searchFileByTthCommand.Parameters.Add("@tth", DbType.StringFixedLength);

            _searchFileByNameCommand = new SQLiteCommand(string.Format("SELECT {0} FROM share WHERE virtual LIKE @query LIMIT @limit", allRows), _connection);
            _searchFileByNameCommand.Parameters.Add("@query", DbType.String);
            _searchFileByNameCommand.Parameters.Add("@limit", DbType.UInt16);

            _deleteFileByTthCommand = new SQLiteCommand("DELETE FROM share WHERE tth = @tth", _connection);
            _deleteFileByTthCommand.Parameters.Add("@tth", DbType.StringFixedLength);

            _deleteFileByIdCommand = new SQLiteCommand("DELETE FROM share WHERE id = @id", _connection);
            _deleteFileByIdCommand.Parameters.Add("@id", DbType.Int32);

            _readFilesCommand = new SQLiteCommand(string.Format("SELECT {0} FROM share", allRows), _connection);

            _readOldestFilesCommand = new SQLiteCommand(string.Format("SELECT {0} FROM share ORDER BY createtime", allRows), _connection);

            _addDirectoryCommand = new SQLiteCommand("INSERT INTO virtualdirs (name, systempath) VALUES (@name, @systempath)", _connection);
            _addDirectoryCommand.Parameters.Add("@name", DbType.String);
            _addDirectoryCommand.Parameters.Add("@systempath", DbType.String);

            _deleteDirectoryCommand = new SQLiteCommand("DELETE FROM virtualdirs WHERE systempath = @systempath", _connection);
            _deleteDirectoryCommand.Parameters.Add("@systempath", DbType.String);

            _addExcludeDirectoryCommand = new SQLiteCommand("INSERT INTO virtualdirsexclude (systempath) VALUES (@systempath)", _connection);
            _addExcludeDirectoryCommand.Parameters.Add("@systempath", DbType.String);

            _deleteExcludeDirectoryCommand = new SQLiteCommand("DELETE FROM virtualdirsexclude WHERE systempath = @systempath", _connection);
            _deleteExcludeDirectoryCommand.Parameters.Add("@systempath", DbType.String);

            _readFilesShortCommand = new SQLiteCommand("SELECT id, systempath, lastwritetime, size FROM share", _connection);

            _readDirectoriesCommand = new SQLiteCommand("SELECT name, systempath FROM virtualdirs", _connection);

            _deleteFilesByFolderCommand = new SQLiteCommand("DELETE FROM share WHERE systempath LIKE @systempath", _connection);
        }

        /// <summary>
        /// Creates database file and required tables
        /// </summary>
        /// <param name="path"></param>
        public SQLiteConnection CreateDataBase(string path)
        {
            DatabasePath = path;
            try
            {
                if (path != ":memory:")
                {
                    if (!Directory.Exists(Path.GetDirectoryName(path)))
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                    SQLiteConnection.CreateFile(path);
                }
                var conn = new SQLiteConnection("Data Source = " + DatabasePath);
                conn.Open();
                CreateDataBase(conn);
                return conn;
            }
            catch (Exception x) 
            {
                OnSqlError(new SQLiteExceptionEventArgs { Exception = x });
                return null;
            }
        }

        private void CreateDataBase(SQLiteConnection conn)
        {
            SQLiteCommand command = conn.CreateCommand();
            command.CommandText =
                    @"CREATE TABLE [share] (
                    [id] integer PRIMARY KEY AUTOINCREMENT NOT NULL,
                    [tth] char(39) NOT NULL,
                    [filename] varchar(256) NOT NULL,
                    [size] long NOT NULL,
                    [virtual] char(100) NOT NULL,
                    [systempath] varchar(256) NOT NULL,
                    [createtime] datetime NOT NULL,
                    [lastwritetime] datetime NOT NULL,
                    [lastaccess] datetime NULL,
                    [uploaded] unsigned bigint NOT NULL
                    );

                    CREATE INDEX IDX_SHARE_TTH on share (tth);
                    CREATE INDEX IDX_SHARE_SYSTEM on share (systempath);
                    CREATE INDEX IDX_SHARE_VIRTUAL on share (virtual);

                    CREATE TABLE [virtualdirs] (
                    [id] integer PRIMARY KEY AUTOINCREMENT NOT NULL,
                    [name] varchar(256) NOT NULL,
                    [systempath] varchar(256) NOT NULL
                    );

                    CREATE TABLE [virtualdirsexclude] ([systempath] varchar(256) PRIMARY KEY NOT NULL);

                    CREATE TABLE [leaves] ( 
                    [id] integer PRIMARY KEY NOT NULL,
                    [data] blob NOT NULL);
                    CREATE INDEX IDX_LEAVES_ID on leaves (id);


";
            command.CommandType = CommandType.Text;
            command.ExecuteNonQuery();
        }
        
        /// <summary>
        /// Returns active connection to SQLite database ()
        /// </summary>
        /// <returns></returns>
        public SQLiteConnection GetConnection()
        {
            if (_connection == null)
            {
                if (DatabasePath == ":memory:" || !File.Exists(DatabasePath))
                {
                    _connection = CreateDataBase(DatabasePath);
                    //Execute("PRAGMA synchronous=OFF;", connection);
                    return _connection;
                }
                var csb = new SQLiteConnectionStringBuilder();
                csb.DataSource = DatabasePath;
                _connection = new SQLiteConnection(csb.ToString()); 
                _connection.Open();

                CreateCommands();
                //Execute("PRAGMA synchronous=OFF;", connection);
            }
            return _connection;
        }

        public void AddFile(ContentItem item)
        {
            try
            {
                lock (_syncRoot)
                {
                    _addFileCommand.Parameters["@tth"].Value = item.Magnet.TTH;
                    _addFileCommand.Parameters["@size"].Value = item.Magnet.Size;
                    _addFileCommand.Parameters["@virtual"].Value = item.VirtualPath;
                    _addFileCommand.Parameters["@systempath"].Value = item.SystemPath;
                    _addFileCommand.Parameters["@lastwritetime"].Value = item.FileLastWrite;
                    _addFileCommand.ExecuteNonQuery();

                    TotalFiles++;
                    TotalShared += item.Magnet.Size;
                }
            }
            catch (Exception x)
            {
                OnSqlError(new SQLiteExceptionEventArgs { Exception = x, SqlCommand = _addFileCommand });
            }
        }

        public void RemoveFile(string tth)
        {
            try
            {
                var ci = this.SearchByTth(tth);

                lock (_syncRoot)
                {
                    if (ci == null)
                        return;

                    TotalFiles--;
                    TotalShared -= ci.Value.Magnet.Size;

                    _deleteFileByTthCommand.Parameters["@tth"].Value = tth;
                    _deleteFileByTthCommand.ExecuteNonQuery();
                }
            }
            catch (Exception x)
            {
                OnSqlError(new SQLiteExceptionEventArgs { Exception = x, SqlCommand = _deleteFileByTthCommand });
            }
        }

        public void AddIgnoreDirectory(string path)
        {
            try
            {
                lock (_syncRoot)
                {
                    if (_excludeDirectories.Contains(path))
                        return;

                    _addExcludeDirectoryCommand.Parameters["@systempath"].Value = path;
                    _addExcludeDirectoryCommand.ExecuteNonQuery();

                    _excludeDirectories.Add(path);
                }
            }
            catch (Exception x)
            {
                OnSqlError(new SQLiteExceptionEventArgs { Exception = x, SqlCommand = _addExcludeDirectoryCommand });
            }
        }

        public void RemoveIgnoreDirectory(string path)
        {
            try
            {
                lock (_syncRoot)
                {
                    if (!_excludeDirectories.Contains(path))
                        return;

                    _deleteExcludeDirectoryCommand.Parameters["@systempath"].Value = path;
                    _deleteExcludeDirectoryCommand.ExecuteNonQuery();

                    _excludeDirectories.Remove(path);
                }
            }
            catch (Exception x)
            {
                OnSqlError(new SQLiteExceptionEventArgs { Exception = x, SqlCommand = _deleteExcludeDirectoryCommand });
            }
        }
        
        public void AddDirectory(string systemPath, string virtualPath = null)
        {
            if (systemPath == null) 
                throw new ArgumentNullException("systemPath");

            if (_excludeDirectories.Contains(systemPath))
                throw new InvalidOperationException("This path is marked as excluded, remove it from exclude list first");

            if (virtualPath == null)
                virtualPath = Path.GetDirectoryName(systemPath);

            try
            {
                lock (_syncRoot)
                {
                    _addDirectoryCommand.Parameters["@name"].Value = virtualPath;
                    _addDirectoryCommand.Parameters["@systempath"].Value = systemPath;
                    _addDirectoryCommand.ExecuteNonQuery();
                }

            }
            catch (Exception x)
            {
                OnSqlError(new SQLiteExceptionEventArgs { Exception = x, SqlCommand = _addDirectoryCommand });
            }
        }

        public void RemoveDirectory(string systemPath)
        {
            try
            {
                lock (_syncRoot)
                {
                    _deleteDirectoryCommand.Parameters["@systempath"].Value = systemPath;
                    _deleteDirectoryCommand.ExecuteNonQuery();
                }
            }
            catch (Exception x)
            {
                OnSqlError(new SQLiteExceptionEventArgs { Exception = x, SqlCommand = _deleteExcludeDirectoryCommand });
            }
        }

        public List<ContentItem> Search(SearchQuery query, int limit = 0)
        {
            SQLiteCommand command = null;

            try
            {
                lock (_syncRoot)
                {
                    if (query.SearchType == SearchType.TTH)
                    {
                        command = _searchFileByTthCommand;
                        _searchFileByTthCommand.Parameters["@tth"].Value = query.Query;
                    }
                    else
                    {
                        command = _searchFileByNameCommand;
                        _searchFileByNameCommand.Parameters["@query"].Value = string.Format("%{0}%", query.Query);
                        _searchFileByNameCommand.Parameters["@limit"].Value = limit == 0 ? short.MaxValue : limit;
                    }
                
                    var list = new List<ContentItem>();
                    using (var reader = (query.SearchType == SearchType.TTH ? _searchFileByTthCommand.ExecuteReader() : _searchFileByNameCommand.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var fileName = reader.GetString(1);

                            if (!SearchHelper.IsFileInCategory(fileName, query.SearchType))
                                continue;

                            list.Add(ReadContentItem(reader));
                        }
                    }
                    return list;
                }
            }
            catch (Exception x)
            {
                OnSqlError(new SQLiteExceptionEventArgs { Exception = x, SqlCommand = command });
                return null;
            }
        }

        private ContentItem ReadContentItem(SQLiteDataReader reader)
        {
            // 0    1         2     3        4           5           6              7           8
            // tth, filename, size, virtual, systempath, createtime, lastwritetime, lastaccess, uploaded
            return new ContentItem
            {
                Magnet = new Magnet { TTH = reader.GetString(0), FileName = reader.GetString(1), Size = reader.GetInt64(2) },
                VirtualPath = reader.GetString(3),
                CreateDate = reader.GetDateTime(5),
                FileLastWrite = reader.GetDateTime(6),
                LastAccess = reader.GetDateTime(7),
                UploadedBytes = (ulong)reader.GetInt64(8)
            };
        }
        
        public void Reload()
        {
            var removeFolders = new List<string>();
            
            // 1. check directories for existance

            using (var reader = _readDirectoriesCommand.ExecuteReader())
            {
                while (reader.Read())
                {
                    var systempath = reader.GetString(1);

                    if (!Directory.Exists(systempath))
                        removeFolders.Add(systempath);
                }
            }

            // 2. remove deleted directories and files inside

            using (var transaction = GetConnection().BeginTransaction())
            {
                foreach (var removeFolder in removeFolders)
                {
                    _deleteDirectoryCommand.Parameters["@systempath"].Value = removeFolder;
                    _deleteDirectoryCommand.ExecuteNonQuery();

                    _deleteFilesByFolderCommand.Parameters["@systempath"].Value = removeFolder + "%";
                    _deleteFilesByFolderCommand.ExecuteNonQuery();
                }
                transaction.Commit();
            }

            if (removeFolders.Count > 0)
            {
                UpdateStats();
            }

            // 3. check current files for existance

            var removeList = new List<int>();

            using (var reader = _readFilesShortCommand.ExecuteReader())
            {
                while (reader.Read())
                {
                    var path = reader.GetString(1);
                    var lastWrite = reader.GetDateTime(2);
                    var size = reader.GetInt64(3);
                    DateTime fileWrite;
                    long fileSize;

                    var exists = FileHelper.FileExists(path, out fileSize, out fileWrite);

                    if (!exists || fileWrite != lastWrite || fileSize != size)
                    {
                        removeList.Add(reader.GetInt32(0));
                        TotalFiles--;
                        TotalShared -= size;
                    }
                }
            }

            // 4. delete all modified or deleted files

            using (var transaction = GetConnection().BeginTransaction())
            {
                foreach (var id in removeList)
                {
                    _deleteFileByIdCommand.Parameters["@id"].Value = id;
                    _deleteFileByIdCommand.ExecuteNonQuery();
                }
                
                transaction.Commit();
            }

            // 5. update all folders for new files

            using (var reader = _readDirectoriesCommand.ExecuteReader())
            {
                while (reader.Read())
                {
                    var name = reader.GetString(0);
                    var systempath = reader.GetString(1);
                    
                    ScanFiles(systempath, name);
                }
            }

            // 6. hash new files

            while (_filesToAdd.Count > 0)
            {
                var file = _filesToAdd.Dequeue();

                byte[][][] tigerTree;

                var tth = HashHelper.GetTTH(file.SystemPath, out tigerTree);
                
                var fi = new FileInfo(file.SystemPath);

                var ci = new ContentItem {
                    Magnet = new Magnet { TTH = tth, FileName = Path.GetFileName(file.SystemPath), Size = fi.Length },
                    CreateDate = DateTime.Now,
                    FileLastWrite = fi.LastWriteTime,
                    LastAccess = DateTime.Now,
                    VirtualPath = file.VirtualPath,
                    SystemPaths = new []{ file.SystemPath }
                };

                AddFile(ci);
            }
        }

        private void ScanFiles(string path, string virtualPath)
        {
            if (_excludeDirectories.Contains(path))
                return;

            foreach (var dir in Directory.EnumerateDirectories(path))
            {
                ScanFiles(dir, Path.Combine(virtualPath, Path.GetDirectoryName(dir)));
            }

            foreach (var filePath in Directory.EnumerateFiles(path))
            {
                _filesToAdd.Enqueue(new FileTask { SystemPath = filePath, VirtualPath = Path.Combine(virtualPath, Path.GetFileName(filePath))});
            }
        }
        
        /// <summary>
        /// Deletes db file, all data will be lost
        /// </summary>
        public void Clear()
        {
            if (File.Exists(DatabasePath))
            {
                if (_connection != null)
                {
                    _connection.Close();
                    _connection.Dispose();
                    _connection = null;
                }

                File.Delete(DatabasePath);

                TotalFiles = 0;
                TotalShared = 0;
            }
        }

        public IEnumerable<ContentItem> Items()
        {
            SQLiteDataReader reader;
            try
            {
                reader = _readFilesCommand.ExecuteReader();
            }
            catch (Exception x)
            {
                OnSqlError(new SQLiteExceptionEventArgs { Exception = x, SqlCommand = _readFilesCommand });
                yield break;
            }

            using (reader)
            {
                while (reader.Read())
                {
                    yield return ReadContentItem(reader);
                }
            }
        }

        public IEnumerable<ContentItem> OldestItems()
        {
            SQLiteDataReader reader;
            try
            {
                reader = _readOldestFilesCommand.ExecuteReader();
            }
            catch (Exception x)
            {
                OnSqlError(new SQLiteExceptionEventArgs { Exception = x, SqlCommand = _readOldestFilesCommand });
                yield break;
            }

            using (reader)
            {
                while (reader.Read())
                {
                    yield return ReadContentItem(reader);
                }
            }
        }
    }

    public class SQLiteExceptionEventArgs : EventArgs
    {
        public Exception Exception { get; set; }
        public SQLiteCommand SqlCommand { get; set; }
    }

    public struct FileTask
    {
        public string SystemPath;
        public string VirtualPath;
    }
}
