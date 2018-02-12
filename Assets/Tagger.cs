using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Video;

public class Tagger : MonoBehaviour {
    [SerializeField] private string _srtPath;

    private VideoPlayer _vid;
    private AudioSource _src;

    private List<Subtitle> _subs;
    private int _subIdx;

    void Awake () {
        _subs = SrtParse.Parse(_srtPath);

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

        Subtitle line = _subs[_subIdx];

        if (Input.GetKeyDown(KeyCode.Space)) {
            if (line != null) {
                PlayFrom(line.Start);
            }
        }

        if (Input.GetKeyDown(KeyCode.S)) {
            GoTo(_subIdx - 1);
        }

        if (Input.GetKeyDown(KeyCode.A)) {
            GoTo(_subIdx - 10);
        }

        if (Input.GetKeyDown(KeyCode.D)) {
            GoTo(_subIdx + 1);
        }

        if (Input.GetKeyDown(KeyCode.F)) {
            GoTo(_subIdx + 10);
        }

        if (_vid.time >= line.End) {
            _vid.Pause();
        }
    }

    private void GoTo(int subIdx) {
        _subIdx = Mathf.Clamp(subIdx, 0, _subs.Count); ;
        PlayFrom(_subs[_subIdx].Start);
    }

    private void PlayFrom(double time) {
        _vid.time = time;
        // Note: Play() happens when OnSeekCompleted is called
    }

    void OnSeekCompleted(VideoPlayer player) {
        _vid.Play();
    }

    private void OnGUI() {
        Subtitle line = _subs[_subIdx];
        if (line != null) {
            for (int i = 0; i < line.Text.Count; i++) {
                GUILayout.Label(line.Text[i]);
            }
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
