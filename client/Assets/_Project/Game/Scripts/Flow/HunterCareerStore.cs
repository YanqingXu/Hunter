using System;
using System.IO;
using System.Text;

namespace BigWorld.Gameplay
{
    /// <summary>Atomic disk replacement. A failed save never publishes the candidate in memory.</summary>
    public sealed class HunterCareerStore
    {
        public string SavePath { get; private set; }
        public bool RecoveredBackup { get; private set; }
        public bool Migrated { get; private set; }
        private readonly Func<string, HunterCareerData> decode;
        private readonly Func<HunterCareerData, string> encode;
        public HunterCareer Current { get; private set; }

        public HunterCareerStore(string path, Func<string, HunterCareerData> decoder, Func<HunterCareerData, string> encoder)
        { SavePath = Path.GetFullPath(path); decode = decoder; encode = encoder; }

        public void Load()
        {
            HunterCareer loaded;
            RecoveredBackup = Migrated = false;
            bool needsSave = false;
            if (!File.Exists(SavePath) && !File.Exists(SavePath + ".bak"))
            { loaded = HunterCareer.CreateNew(); needsSave = true; }
            else
            {
                try { loaded = Read(SavePath); }
                catch (NotSupportedException) { throw; } // Never roll a newer profile back to an older backup.
                catch (Exception error)
                {
                    if (!(error is IOException) && !(error is ArgumentException)) throw;
                    loaded = Read(SavePath + ".bak");
                    RecoveredBackup = true;
                    // Keep evidence of the damaged primary; do not replace the known-good backup with it.
                    if (File.Exists(SavePath)) File.Move(SavePath, SavePath + ".corrupt-" + Guid.NewGuid().ToString("N"));
                    needsSave = true;
                }
            }
            if (loaded.InRun)
            {
                if (!loaded.Settle(CareerOutcome.Interrupted, 0)) throw new InvalidOperationException("未完成的出战记录无法结算。");
                needsSave = true;
            }
            // Migration/write failures are not evidence of corrupt input. Never fall back or publish on them.
            if (needsSave || Migrated) Save(loaded);
            Current = loaded;
        }

        private HunterCareer Read(string path)
        {
            var source = decode(File.ReadAllText(path, Encoding.UTF8));
            var loaded = new HunterCareer(source);
            Migrated = source.Version != HunterCareer.CurrentVersion;
            return loaded;
        }

        public bool Change(Func<HunterCareer, bool> command)
        {
            if (Current == null) throw new InvalidOperationException("存档尚未加载。");
            var candidate = new HunterCareer(Current.Snapshot());
            if (!command(candidate)) return false;
            Save(candidate); Current = candidate; return true;
        }

        private void Save(HunterCareer candidate)
        {
            var snapshot = candidate.Snapshot(); HunterCareer.Validate(snapshot);
            Directory.CreateDirectory(Path.GetDirectoryName(SavePath));
            string temporary = SavePath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(encode(snapshot));
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                if (File.Exists(SavePath)) File.Replace(temporary, SavePath, SavePath + ".bak");
                else File.Move(temporary, SavePath);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
