using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Video;

public class Tagger : MonoBehaviour {
    [SerializeField] private string _srtPath;

    private VideoPlayer _vid;
    private AudioSource _src;

    private List<SubMeta> _metasubs;
    private int _index;

    void Awake () {
        var subs = SrtParse.Load(_srtPath);
        SrtParse.Sanitize(subs);
        _metasubs = new List<SubMeta>(subs.Count);
        for (int i = 0; i < subs.Count; i++) {
            _metasubs.Add(new SubMeta() {
                Subtitle = subs[i]
            });
        }

        _src = gameObject.GetComponent<AudioSource>();
        _vid = gameObject.GetComponent<VideoPlayer>();

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
        _index = Mathf.Clamp(subIdx, 0, _metasubs.Count); ;
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
                }
            }
            GUILayout.EndArea();

            GUILayout.BeginArea(new Rect(Screen.width/2f - 300f, Screen.height - 100f, 600f, 100), GUI.skin.box);
            GUILayout.Label(m.Character + ": ");
            GUILayout.Space(10f);
            for (int i = 0; i < m.Subtitle.Text.Count; i++) {
                GUILayout.Label(m.Subtitle.Text[i]);
            }
            GUILayout.EndArea();

            GUILayout.BeginArea(new Rect(Screen.width - 100f, Screen.height-50, 100f, 50f), GUI.skin.box);
            if (GUILayout.Button("Save")) {
                SrtParse.Save(_metasubs, _vid.clip.originalPath.Replace(".mp4", "_custom.srt"));
            }
            GUILayout.EndArea();
        }
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
                /* Remove meta crud like:
                 * {\move(10,10,190,230,100,400)\fad(0,1
                 */
                if (sub.Text[j].Contains("{\\")) {
                    subs.RemoveAt(i);
                    i--;
                    break;
                }

                /* Remove non-verbal cues
                 * (yells)
                 * Todo: handle (man yells) Let me see them titties!
                 */
                if (sub.Text[j].Contains("(")) {
                    Debug.Log("Removing: " + sub.Text[j]);
                    sub.Text.RemoveAt(j);
                    j=0;
                    if (sub.Text.Count == 0) {
                        break;
                    }
                }

                /* Remove off-screen speaker clarification
                 * Dan: Hey, hey you!
                 * We're encoding a much more thorough version of this
                 */
                if (sub.Text[j].Contains(":")) {
                    Debug.Log("Removing: " + sub.Text[j]);
                    sub.Text[j] = sub.Text[j].Remove(0, sub.Text[j].IndexOf(":")+1);
                    if (sub.Text[j] == "") {
                        sub.Text.RemoveAt(j);
                        j = 0;
                        if (sub.Text.Count == 0) {
                            break;
                        }
                    }
                }

                /* Todo: handle multispeaker dashed notation */
                if (sub.Text[j].StartsWith("- ")) {
                    Debug.Log("Removing: " + sub.Text[j]);
                    sub.Text.RemoveAt(j);
                    j = 0;
                    if (sub.Text.Count == 0) {
                        break;
                    }
                }

                /* Finally, filter out left-over special characters, such as in:
                 * ♪ Kick off your high heels ♪
                 */
                sub.Text[j] = RemoveSpecialCharacters(sub.Text[j]);
            }
        }
    }

    public static string RemoveSpecialCharacters(this string str) {
        StringBuilder sb = new StringBuilder();
        foreach (char c in str) {
            if (
                (c >= '0' && c <= '9') ||
                (c >= 'A' && c <= 'Z') ||
                (c >= 'a' && c <= 'z') ||
                c == ' ' || c == '.' || c == ',' || c == '!' || c == '\'') {
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

        Debug.Log("Saved to: " + path);
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
