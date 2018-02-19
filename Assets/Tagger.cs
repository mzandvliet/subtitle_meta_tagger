using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.Video;

/* Todo:
 * - No need to export back to SRT
 * - Also no need to go back to python to use the produce metadata to split the video
 * and create the training sample database.
 * 
 * We can do all of those right here in this tool
 * We can also save our meta with a custom text file more amenable to our goals.
 * 
 * We could call ffmpeg from here, for example.
 * 
 * - Put in a victory sound when we complete tagging a whole episode (we're going to
 * be doing this a lot)
 * 
 * 
 * Note that in the future we will probably want to load old-format data and add to
 * it or transform it, for new experiments. Make sure you don't have to do all your
 * tagging work over again.
 */

public class Tagger : MonoBehaviour {
    private VideoPlayer _vid;
    private AudioSource _src;

    private List<SubMeta> _metasubs;
    private int _index;

    private bool _isDirty;

    void Awake () {
        _src = gameObject.GetComponent<AudioSource>();
        _vid = gameObject.GetComponent<VideoPlayer>();

        string srtPath = _vid.clip.originalPath.Replace("_.mp4", "_eng.srt");
        var subs = SrtParse.Load(srtPath);
        SrtParse.Sanitize(subs);
        _metasubs = new List<SubMeta>(subs.Count);
        for (int i = 0; i < subs.Count; i++) {
            _metasubs.Add(new SubMeta() {
                Subtitle = subs[i]
            });
        }

        _vid.seekCompleted += OnSeekCompleted;
        _vid.prepareCompleted += OnPrepared;
        _vid.Prepare();
    }

    void OnPrepared(VideoPlayer player) {
        _vid.controlledAudioTrackCount = _vid.audioTrackCount;
        for (ushort i = 0; i < _vid.audioTrackCount; i++) {
            _vid.EnableAudioTrack(i, true);
            _vid.SetTargetAudioSource(i, _src);
        }

        // Hack: without this play/pause, first time setting _vids.time in PlayFrom fails
        _vid.Play();
        _vid.Pause();
        
        PlayFrom(_metasubs[0].Subtitle.Start);
    }

    void Update() {
        if (!_vid.isPrepared) {
            return;
        }

        SubMeta m = _metasubs[_index];

        if (Input.GetKeyDown(KeyCode.Space)) {
            if (m != null) {
                PlayFrom(m.Subtitle.Start);
            }
        }

        if (Input.GetKeyDown(KeyCode.S)) {
            GoTo(_index - 1);
        }

        if (Input.GetKeyDown(KeyCode.A)) {
            GoTo(_index - 10);
        }

        if (Input.GetKeyDown(KeyCode.D)) {
            GoTo(_index + 1);
        }

        if (Input.GetKeyDown(KeyCode.F)) {
            GoTo(_index + 10);
        }

        if (_vid.time >= m.Subtitle.End + 0.25f) {
            _vid.Pause();
        }
    }

    private void GoTo(int subIdx) {
        _index = Mathf.Clamp(subIdx, 0, _metasubs.Count-1);
        PlayFrom(_metasubs[_index].Subtitle.Start);
    }

    private void PlayFrom(double time) {
        _vid.time = time - 0.1f;
        // Note: Play() happens when OnSeekCompleted is called
    }

    void OnSeekCompleted(VideoPlayer player) {
        _vid.Play();
    }

    private void OnGUI() {
        SubMeta m = _metasubs[_index];
        if (m != null) {
            GUILayout.BeginArea(new Rect(Screen.width - 100f, 0f, 100f, Screen.height), GUI.skin.box);
            GUILayout.Label(_index + "/" + _metasubs.Count);
            for (int i = 0; i < 18; i++) {
                GUI.color = (DeadwoodChar)i == m.Character ? Color.blue : Color.white;
                if (GUILayout.Button(((DeadwoodChar)i).ToString())) {
                    m.Character = (DeadwoodChar)i;
                    _isDirty = true;
                }
            }
            GUILayout.EndArea();

            GUI.color = Color.white;

            GUILayout.BeginArea(new Rect(Screen.width/2f - 300f, Screen.height - 100f, 600f, 100), GUI.skin.box);
            GUILayout.Label(m.Character + ": ");
            GUILayout.Space(10f);
            for (int i = 0; i < m.Subtitle.Text.Count; i++) {
                GUILayout.Label(m.Subtitle.Text[i]);
            }
            GUILayout.EndArea();

            GUILayout.BeginArea(new Rect(Screen.width - 100f, Screen.height-100, 100f, 100f), GUI.skin.box);
            if (GUILayout.Button("Load")) {
                LoadJSON();
            }
            GUILayout.Space(32);
            if (GUILayout.Button("Save")) {
                Save();
            }
            GUILayout.EndArea();
        }
    }

    private void LoadJSON() {
        if (_isDirty) {
            if (!EditorUtility.DisplayDialog("Unsaved changes", "Unsaved changed will be lost if you load, continue?", "Load", "Cancel")) {
                return;
            }
        }

        var path = _vid.clip.originalPath.Replace(".mp4", "_.json");
        if (!File.Exists(path)) {
            Debug.Log("No existing JSON file found");
            return;
        }

        _metasubs = SrtParse.LoadJson(path);
        _isDirty = false;
    }

    private void Save() {
        var path = _vid.clip.originalPath.Replace(".mp4", "_.json");
        if (File.Exists(path)) {
            if (!EditorUtility.DisplayDialog("File exists", "Overwrite existing file?", "Save", "Cancel")) {
                return;
            }
        }

        SrtParse.Save(_metasubs, _vid.clip.originalPath.Replace(".mp4", "_custom.srt"));
        SrtParse.SaveJson(_metasubs, path);
        _isDirty = false;
    }
}

public static class SrtParse {
    public static List<Subtitle> Load(string path) {
        var reader = File.OpenText(path);

        List<Subtitle> subs = new List<Subtitle>();
        Subtitle sub = null;

        while (!reader.EndOfStream) {
            var line = reader.ReadLine();
            var p = GetCase(line);

            switch (p) {
                case ParseCase.Id:
                    // New ID found, complete previous and create new
                    if (sub != null) {
                        subs.Add(sub);
                    }
                    sub = new Subtitle();
                    sub.Index = int.Parse(line);
                    break;
                case ParseCase.Time:
                    var pts = line.Split(new[] { " --> " }, StringSplitOptions.RemoveEmptyEntries);
                    sub.Start = ReadTimeSecs(pts[0]);
                    sub.End = ReadTimeSecs(pts[1]);
                    break;
                case ParseCase.Other:
                    sub.Text.Add(line);
                    break;
                case ParseCase.Empty:
                default:
                    continue;
            }
        }

        reader.Close();

        return subs;
    }

    private static ParseCase GetCase(string line) {
        if (line == "") {
            return ParseCase.Empty;
        }

        int bla;
        if (int.TryParse(line, out bla)) {
            return ParseCase.Id;
        }

        if (line.Contains("-->")) {
            return ParseCase.Time;
        }

        return ParseCase.Other;
    }

    public static void Sanitize(List<Subtitle> subs) {
        for (int i = 0; i < subs.Count; i++) {
            var sub = subs[i];
            for (int j = 0; j < sub.Text.Count; j++) {
                /* Remove meta crud entries, like:
                 * {\move(10,10,190,230,100,400)\fad(0,1
                 */
                if (sub.Text[j].Contains("{\\")) {
                    Debug.Log("Removing meta: " + sub.Text[j]);
                    subs.RemoveAt(i);
                    i--;
                    break;
                }

                /* For now, remove multispeaker dashed notation entries, like:
                 * - Hey Dan!
                 * - Hello Elsworth...
                 */
                if (sub.Text[j].StartsWith("- ")) {
                    Debug.Log("Removing dashed: " + sub.Text[j]);
                    subs.RemoveAt(i);
                    i--;
                    break;
                }

                /* Remove non-verbal cues, like:
                 * (yells)
                 * Todo: handle (man yells) Let me see them titties!
                 */
                if (sub.Text[j].Contains("(")) {
                    Debug.Log("Removing non-verbal: " + sub.Text[j]);
                    subs.RemoveAt(i);
                    i--;
                    break;
                }

                /* Remove off-screen speaker clarification, like:
                 * Dan: Hey, hey you!
                 */
                if (sub.Text[j].Contains(":")) {
                    Debug.Log("Removing clarification: " + sub.Text[j]);
                    sub.Text[j] = sub.Text[j].Remove(0, sub.Text[j].IndexOf(":")+1);
                    if (sub.Text[j] == "") {
                        sub.Text.RemoveAt(j);
                        j = 0;
                        continue;
                    }
                }

                /* Finally, filter out left-over special characters, such as in:
                 * ♪ Kick off your high heels ♪
                 */
                sub.Text[j] = RemoveSpecialCharacters(sub.Text[j]);
            }
        }

        // Merge multiline
        for (int i = 0; i < subs.Count; i++) {
            var sub = subs[i];
            string line = "";
            for (int j = 0; j < sub.Text.Count; j++) {
                line += " " + sub.Text[j];
            }

            sub.Text.Clear();
            sub.Text.Add(line);
        }
    }

    public static string RemoveSpecialCharacters(this string str) {
        StringBuilder sb = new StringBuilder();
        foreach (char c in str) {
            if (
                (c >= '0' && c <= '9') ||
                (c >= 'A' && c <= 'Z') ||
                (c >= 'a' && c <= 'z') ||
                c == ' ' || c == '.' || c == ',' || c == '!' || c == '?' || c == '\'') {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private static double ReadTimeSecs(string time) {
        var pts = time.Split(new[] { ":", "," }, StringSplitOptions.RemoveEmptyEntries);
        double hrs = int.Parse(pts[0]) * 3600.0;
        double mns = int.Parse(pts[1]) * 60.0;
        double scs = int.Parse(pts[2]) * 1.0;
        double mls = int.Parse(pts[3]) / 1000.0;
        return hrs + mns + scs + mls;
    }

    public static void Save(List<SubMeta> meta, string path) {
        var writer = new StreamWriter(path);

        for (int i = 0; i < meta.Count; i++) {
            SubMeta m = meta[i];
            writer.WriteLine(m.Subtitle.Index);
            string time = TimeToString(m.Subtitle.Start) + " --> " + TimeToString(m.Subtitle.End);
            writer.WriteLine(time);
        
            writer.WriteLine("Speaker: " + m.Character);
        
            for (int j = 0; j < m.Subtitle.Text.Count; j++) {
                writer.WriteLine(m.Subtitle.Text[j]);
            }
        
            writer.WriteLine("");
        }

        writer.Close();

        Debug.Log("Saved SRT to: " + path);
    }

    private static string TimeToString(double time) {
        int hrs = (int)(time / 3600.0);
        time -= hrs * 3600.0;
        int mns = (int)(time / 60.0);
        time -= mns * 60.0;
        int scs = (int)time;
        time -= scs * 1.0;
        int mls = (int)(time * 1000.0);
        return string.Format("{0:00}:{1:00}:{2:00},{3:000}", hrs, mns, scs, mls);
    }

    public static void SaveJson(List<SubMeta> meta, string path) {
        var writer = new StreamWriter(path);

        //        for (int i = 0; i < meta.Count; i++) {
        //            SubMeta m = meta[i];
        //            writer.WriteLine(m.Subtitle.Index);
        //            writer.WriteLine(m.Subtitle.Start);
        //            writer.WriteLine(m.Subtitle.End);
        //            writer.WriteLine(m.Character);
        //            for (int j = 0; j < m.Subtitle.Text.Count; j++) {
        //                writer.WriteLine(m.Subtitle.Text[j]);
        //            }
        //
        //            writer.WriteLine("");
        //        }

        string json = JsonConvert.SerializeObject(meta);
        writer.WriteLine(json);

        writer.Close();

        Debug.Log("Saved Meta to: " + path);
    }

    public static List<SubMeta> LoadJson(string path) {
        var json = File.ReadAllText(path);
        var meta = JsonConvert.DeserializeObject<List<SubMeta>>(json);
        Debug.Log("Loaded meta from: " + path);

        return meta;
    }
}

public enum ParseCase {
    Empty,
    Id,
    Time,
    Other
}

public class Subtitle {
    public List<string> Text;
    public int Index;
    public double Start;
    public double End;

    public Subtitle() {
        Text = new List<string>();
    }
}

// https://en.wikipedia.org/wiki/List_of_Deadwood_characters
public enum DeadwoodChar {
    Undetermined,
    BullockS,
    Swearengen,
    GarretA,
    Ellsworth,
    Dority,
    Stubbs,
    Cochran,
    BullockM,
    Star,
    Merrick,
    Trixie,
    Nuttal,
    Farnum,
    Canary,
    Utter,
    Tolliver,
    Hickok
}

public class SubMeta {
    public Subtitle Subtitle;
    public DeadwoodChar Character;
}
