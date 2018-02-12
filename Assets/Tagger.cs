using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Video;

public class Tagger : MonoBehaviour {
    [SerializeField] private string _srtPath;

    private VideoPlayer _vid;
    private AudioSource _src;

    private List<SubMeta> _metasubs;
    private int _index;

    void Awake () {
        var subs = SrtParse.Parse(_srtPath);
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
        Debug.Log("Playback ready");
        _vid.Play();
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

        if (_vid.time >= m.Subtitle.End) {
            _vid.Pause();
        }
    }

    private void GoTo(int subIdx) {
        _index = Mathf.Clamp(subIdx, 0, _metasubs.Count); ;
        PlayFrom(_metasubs[_index].Subtitle.Start);
    }

    private void PlayFrom(double time) {
        _vid.time = time;
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
        }
    }
}

public static class SrtParse {
    public static List<Subtitle> Parse(string _srtPath) {
        var reader = File.OpenText(_srtPath);

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
                    break;
                case ParseCase.Time:
                    var pts = line.Split(new[] { " --> " }, StringSplitOptions.RemoveEmptyEntries);
                    sub.Start = ReadTimeSecs(pts[0]) - 0.1f;
                    sub.End = ReadTimeSecs(pts[1]) + 0.25f;
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

    private static double ReadTimeSecs(string time) {
        var pts = time.Split(new[] { ":", "," }, StringSplitOptions.RemoveEmptyEntries);
        double hrs = int.Parse(pts[0]) * 3600.0;
        double mns = int.Parse(pts[1]) * 60.0;
        double scs = int.Parse(pts[2]) * 1.0;
        double mls = int.Parse(pts[3]) / 1000.0;
        return hrs + mns + scs + mls;
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
