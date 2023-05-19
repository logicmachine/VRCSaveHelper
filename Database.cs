using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using Microsoft.Data.Sqlite;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;

namespace VRCSaveHelper
{
    public class HistoryViewModel
    {
        private Int64 id;
        private string worldId;
        private DateTime timestamp;
        private string data;

        public HistoryViewModel(Int64 id, string worldId, DateTime timestamp, string data)
        {
            this.id = id;
            this.worldId = worldId;
            this.timestamp = timestamp;
            this.data = data;
        }

        public Int64 Id
        {
            get { return id; }
            set { id = value; }
        }

        public string WorldId
        {
            get { return worldId; }
        }

        public DateTime Timestamp
        {
            get { return timestamp; }
        }

        public string DisplayTimestamp
        {
            get { return Timestamp.ToString("u"); }
        }

        public string Data
        {
            get { return data; }
        }
    }

    public class WorldViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private Database database;
        private string id;
        private string name;
        private bool autoLoad;
        private bool autoSave;
        private ObservableCollection<HistoryViewModel> history;

        public WorldViewModel(Database database, string id, string name, bool autoLoad, bool autoSave, IEnumerable<HistoryViewModel> history)
        {
            this.database = database;
            this.id = id;
            this.name = name;
            this.autoLoad = autoLoad;
            this.autoSave = autoSave;
            this.history = new ObservableCollection<HistoryViewModel>(history);
            this.history.CollectionChanged += History_CollectionChanged;
        }

        private void History_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (var item in e.OldItems)
                {
                    var history = (HistoryViewModel)item;
                    database.RemoveHistory(history);
                }
            }
            if(e.NewItems != null)
            {
                foreach(var item in e.NewItems)
                {
                    var history = (HistoryViewModel)item;
                    database.InsertHistory(ref history);
                }
            }
        }

        public string Id
        {
            get { return id; }
            set
            {
                if (id == value) { return; }
                id = value;
                OnPropertyChanged();
                OnPropertyChanged("DisplayName");
                database?.UpdateWorldProperties(this);
            }
        }

        public string Name
        {
            get { return name; }
            set
            {
                if (name == value) { return; }
                name = value;
                OnPropertyChanged();
                OnPropertyChanged("DisplayName");
                database?.UpdateWorldProperties(this);
            }
        }

        public bool AutoLoad
        {
            get { return autoLoad; }
            set
            {
                if (autoLoad == value) { return; }
                autoLoad = value;
                OnPropertyChanged();
                database?.UpdateWorldProperties(this);
            }
        }

        public bool AutoSave
        {
            get { return autoSave; }
            set
            {
                if(autoSave == value) { return; }
                autoSave = value;
                OnPropertyChanged();
                database?.UpdateWorldProperties(this);
            }
        }

        public string DisplayName
        {
            get { return string.Format("{0} ({1})", name, id); }
        }

        public ObservableCollection<HistoryViewModel> History
        {
            get { return history; }
        }

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class MainViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private Database database;
        private ObservableCollection<WorldViewModel> worlds;
        private WorldViewModel? selectedWorld;
        private HistoryViewModel? selectedHistory;

        public MainViewModel(Database database, IEnumerable<WorldViewModel> worlds)
        {
            this.database = database;
            this.worlds = new ObservableCollection<WorldViewModel>(worlds);
            this.worlds.CollectionChanged += Worlds_CollectionChanged;
            this.selectedWorld = null;
            this.selectedHistory = null;
        }

        private void Worlds_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (var item in e.OldItems)
                {
                    var world = (WorldViewModel)item;
                    database.RemoveWorld(world);
                }
            }
            if(e.NewItems != null)
            {
                foreach(var item in e.NewItems)
                {
                    var world = (WorldViewModel)item;
                    database.InsertWorld(world);
                }
            }
        }

        public ObservableCollection<WorldViewModel> Worlds
        {
            get { return worlds; }
        }

        public WorldViewModel? SelectedWorld
        {
            get { return selectedWorld; }
            set
            {
                if (selectedWorld == value) { return; }
                selectedWorld = value;
                OnPropertyChanged();
            }
        }

        public HistoryViewModel? SelectedHistory
        {
            get { return selectedHistory; }
            set
            {
                if (selectedHistory == value) { return; }
                selectedHistory = value;
                OnPropertyChanged();
            }
        }

        public WorldViewModel? FindWorldById(string id)
        {
            foreach (var world in Worlds)
            {
                if (world.Id == id) { return world; }
            }
            return null;
        }

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class Database
    {
        private SqliteConnection? _connection;

        public Database()
        {
            OpenConnection();
            InitializeDatabase();
        }

        private void OpenConnection()
        {
            var module = Process.GetCurrentProcess().MainModule;
            if (module == null) { return; }
            var directory = Path.GetDirectoryName(module.FileName);
            if (directory == null) { return; }
            var path = Path.Combine(directory, "database.db");
            _connection = new SqliteConnection("Data Source=" + path);
            _connection.Open();
        }

        private void InitializeDatabase()
        {
            if (_connection == null) { return; }

            var worlds_command = _connection.CreateCommand();
            worlds_command.CommandText =
                "CREATE TABLE IF NOT EXISTS worlds (" +
                "  id TEXT NOT NULL PRIMARY KEY," +
                "  name TEXT NOT NULL," +
                "  auto_load INTEGER NOT NULL," +
                "  auto_save INTEGER NOT NULL)";
            worlds_command.ExecuteNonQuery();

            var history_command = _connection.CreateCommand();
            history_command.CommandText =
                "CREATE TABLE IF NOT EXISTS histories (" +
                "  id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT," +
                "  world_id TEXT NOT NULL," +
                "  timestamp DATETIME NOT NULL," +
                "  data TEXT NOT NULL," +
                "  FOREIGN KEY (world_id) REFERENCES worlds(id))";
            history_command.ExecuteNonQuery();
        }

        public void Close()
        {
            _connection?.Close();
            _connection = null;
        }

        public MainViewModel LoadData()
        {
            if (_connection == null) { return new MainViewModel(this, new WorldViewModel[0]); }

            var historyCommand = _connection.CreateCommand();
            historyCommand.CommandText = "SELECT id, world_id, timestamp, data FROM histories";
            var histories = new Dictionary<string, IList<HistoryViewModel>>();
            using (var reader = historyCommand.ExecuteReader())
            {
                while (reader.Read())
                {
                    var id = reader.GetInt64(0);
                    var world = reader.GetString(1);
                    var timestamp = reader.GetDateTime(2);
                    var data = reader.GetString(3);
                    var history = new HistoryViewModel(id, world, timestamp, data);
                    if (histories.ContainsKey(world))
                    {
                        histories[world].Add(history);
                    }
                    else
                    {
                        histories.Add(world, new List<HistoryViewModel>() { history });
                    }
                }
            }

            var worlds = new List<WorldViewModel>();
            var worldCommand = _connection.CreateCommand();
            worldCommand.CommandText = "SELECT id, name, auto_load, auto_save FROM worlds";
            using (var reader = worldCommand.ExecuteReader())
            {
                while (reader.Read())
                {
                    var id = reader.GetString(0);
                    var name = reader.GetString(1);
                    var autoLoad = (reader.GetInt64(2) != 0);
                    var autoSave = (reader.GetInt64(3) != 0);
                    var history = histories.ContainsKey(id) ? histories[id] : new List<HistoryViewModel>();
                    var world = new WorldViewModel(this, id, name, autoLoad, autoSave, history);
                    worlds.Add(world);
                }
            }
            return new MainViewModel(this, worlds);
        }

        public void InsertWorld(WorldViewModel world)
        {
            if (_connection == null) { return; }

            var command = _connection.CreateCommand();
            command.CommandText =
                "INSERT INTO worlds" +
                "  (id, name, auto_load, auto_save)" +
                "  values" +
                "  (:id, :name, :auto_load, :auto_save)";
            command.Parameters.Add("id", SqliteType.Text).Value = world.Id;
            command.Parameters.Add("name", SqliteType.Text).Value = world.Name;
            command.Parameters.Add("auto_load", SqliteType.Integer).Value = world.AutoLoad ? 1 : 0;
            command.Parameters.Add("auto_save", SqliteType.Integer).Value = world.AutoSave ? 1 : 0;
            command.ExecuteNonQuery();
        }

        public void RemoveWorld(WorldViewModel world)
        {
            if (_connection == null) { return; }

            var command = _connection.CreateCommand();
            command.CommandText = "DELETE FROM worlds WHERE id=:id";
            command.Parameters.Add("id", SqliteType.Text).Value = world.Id;
            command.ExecuteNonQuery();
        }

        public void UpdateWorldProperties(WorldViewModel world)
        {
            if (_connection == null) { return; }

            var command = _connection.CreateCommand();
            command.CommandText =
                "UPDATE worlds" +
                "  SET   name=:name," +
                "        auto_load=:auto_load," +
                "        auto_save=:auto_save" +
                "  WHERE id=:id";
            command.Parameters.Add("id", SqliteType.Text).Value = world.Id;
            command.Parameters.Add("name", SqliteType.Text).Value = world.Name;
            command.Parameters.Add("auto_load", SqliteType.Integer).Value = world.AutoLoad ? 1 : 0;
            command.Parameters.Add("auto_save", SqliteType.Integer).Value = world.AutoSave ? 1 : 0;
            command.ExecuteNonQuery();
        }

        public void InsertHistory(ref HistoryViewModel history)
        {
            if (_connection == null) { return; }

            var command = _connection.CreateCommand();
            command.CommandText =
                "INSERT INTO histories" +
                "  (world_id, timestamp, data)" +
                "  values" +
                "  (:world_id, :timestamp, :data);" +
                "SELECT LAST_INSERT_ROWID()";
            command.Parameters.Add("world_id", SqliteType.Text).Value = history.WorldId;
            command.Parameters.Add("timestamp", SqliteType.Text).Value = history.Timestamp;
            command.Parameters.Add("data", SqliteType.Text).Value = history.Data;
            history.Id = Convert.ToInt64(command.ExecuteScalar());
        }

        public void RemoveHistory(HistoryViewModel history)
        {
            if (_connection == null) { return; }

            var command = _connection.CreateCommand();
            command.CommandText = "DELETE FROM histories WHERE id=:id";
            command.Parameters.Add("id", SqliteType.Integer).Value = history.Id;
            command.ExecuteNonQuery();
        }
    }
}
