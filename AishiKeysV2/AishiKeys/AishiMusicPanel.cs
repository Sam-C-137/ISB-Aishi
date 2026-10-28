using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace AishiKeys
{
    public sealed class AishiMusicPanel : MonoBehaviour
    {
        private static readonly Color Gold = new Color(.75f, .71f, .56f);
        private static readonly Color Muted = new Color(.52f, .57f, .56f);
        private AishiMusicPlayer _player;
        private TMP_FontAsset _font;
        private RectTransform _content, _playlistContent, _panelRect;
        private TMP_InputField _newPlaylist;
        private TMP_Text _shuffleLabel, _libraryLabel;
        private TMP_Text _connection, _title, _status, _clock, _volumeLabel, _playLabel;
        private AishiMusicSeekSlider _seek;
        private MusicButton _play, _previous, _next;
        private Slider _volume;
        private readonly List<Image> _rows = new List<Image>();
        private int _selected = -2;
        private float _nextRefresh;
        internal static AishiMusicPanel Create(AishiMusicPlayer player)
        {
            var go = new GameObject("AishiPersonalMusic", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32760;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var view = go.AddComponent<AishiMusicPanel>();
            view._player = player;
            view._font = Resources.FindObjectsOfTypeAll<TMP_Text>().FirstOrDefault(t => t.font != null)?.font ?? TMP_Settings.defaultFontAsset;
            view.Build();
            return view;
        }

        private RectTransform Rect(string name, Transform parent, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        private Image Fill(RectTransform rt, Color color)
        {
            var image = rt.gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        private TMP_Text Text(Transform parent, string value, float x, float y, float w, float h, float size, Color color)
        {
            var text = Rect("Label", parent, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
            text.font = _font;
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.richText = false;
            text.enableWordWrapping = false;
            text.raycastTarget = false;
            return text;
        }

        private Button Button(Transform parent, string title, float x, float y, float w, float h, Action action)
        {
            var rt = Rect(title, parent, x, y, w, h);
            var image = Fill(rt, new Color(.12f, .15f, .15f, .98f));
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.highlightedColor = new Color(.8f, .78f, .64f);
            colors.pressedColor = new Color(.6f, .58f, .48f);
            colors.disabledColor = new Color(.45f, .45f, .45f, .55f);
            button.colors = colors;
            var label = Text(rt, title, 10, 0, w - 20, h, 17, Gold);
            label.alignment = TextAlignmentOptions.Center;
            button.onClick.AddListener(() => action());
            return button;
        }

        private MusicButton Control(Transform parent, string title, float x, float y, float w, float h, Action action)
        {
            var rect = Rect(title, parent, x, y, w, h);
            var native = AishiNativeMusicButton.Create(rect, title, action, _font);
            if (native != null)
                return native;
            Destroy(rect.gameObject);
            var fallback = Button(parent, title, x, y, w, h, action);
            return new MusicButton(fallback, fallback.GetComponentInChildren<TMP_Text>());
        }

        private RectTransform Scroll(Transform parent, string name, float x, float y, float width, float height)
        {
            var scroll = Rect(name, parent, x, y, width, height);
            Fill(scroll, new Color(.065f, .08f, .08f));
            var view = Rect("Viewport", scroll, 0, 0, width, height);
            view.gameObject.AddComponent<RectMask2D>();
            var content = Rect("Content", view, 0, 0, width, height);
            var scroller = scroll.gameObject.AddComponent<ScrollRect>();
            scroller.viewport = view;
            scroller.content = content;
            scroller.horizontal = false;
            scroller.vertical = true;
            scroller.movementType = ScrollRect.MovementType.Clamped;
            scroller.scrollSensitivity = 30;
            return content;
        }

        private void Build()
        {
            var shade = Rect("Backdrop", transform, 0, 0, 0, 0);
            shade.anchorMin = Vector2.zero;
            shade.anchorMax = Vector2.one;
            shade.offsetMin = shade.offsetMax = Vector2.zero;
            Fill(shade, new Color(0, 0, 0, .64f));
            var panel = Rect("MusicPlayer", transform, 0, 0, 900, 680);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, .5f);
            panel.anchoredPosition = Vector2.zero;
            _panelRect = panel;
            panel.gameObject.AddComponent<RectMask2D>();
            Fill(panel, new Color(.035f, .045f, .047f, .99f));
            Fill(Rect("Accent", panel, 0, 0, 900, 2), Gold).raycastTarget = false;
            Text(panel, AishiMusicText.Get("title", "AISHI  /  PERSONAL AUDIO"), 24, 15, 780, 32, 23, Gold);
            Control(panel, "X", 844, 14, 36, 32, () => _player.ClosePlayer());
            _connection = Text(panel, "", 24, 50, 830, 22, 13, Muted);
            _title = Text(panel, AishiMusicText.Get("noTrack", "NO TRACK SELECTED"), 24, 84, 852, 40, 25, new Color(.84f, .85f, .82f));
            _status = Text(panel, "", 24, 124, 852, 25, 14, Muted);
            var seek = Rect("TrackPosition", panel, 24, 149, 852, 24);
            Fill(seek, Color.clear);
            _seek = seek.gameObject.AddComponent<AishiMusicSeekSlider>();
            _seek.minValue = 0;
            _seek.maxValue = 1;
            _seek.wholeNumbers = false;
            Fill(Rect("Rail", seek, 4, 10, 844, 4), new Color(.18f, .21f, .2f)).raycastTarget = false;
            var seekFillArea = Rect("FillArea", seek, 4, 10, 844, 4);
            var seekFill = Rect("Fill", seekFillArea, 0, 0, 0, 0);
            seekFill.anchorMin = Vector2.zero;
            seekFill.anchorMax = Vector2.one;
            seekFill.offsetMin = seekFill.offsetMax = Vector2.zero;
            Fill(seekFill, Gold).raycastTarget = false;
            _seek.fillRect = seekFill;
            var seekHandleArea = Rect("HandleArea", seek, 4, 0, 844, 24);
            var seekHandle = Rect("Handle", seekHandleArea, 0, 0, 10, 18);
            seekHandle.pivot = new Vector2(.5f, .5f);
            seekHandle.anchorMin = seekHandle.anchorMax = new Vector2(0, .5f);
            seekHandle.anchoredPosition = Vector2.zero;
            _seek.targetGraphic = Fill(seekHandle, Gold);
            _seek.handleRect = seekHandle;
            _seek.onValueChanged.AddListener(position => _player.Seek(position));
            _clock = Text(panel, "0:00 / 0:00", 24, 172, 190, 22, 13, Muted);
            _previous = Control(panel, AishiMusicText.Get("previous", "PREVIOUS"), 24, 207, 180, 40, () => _player.Previous());
            _play = Control(panel, AishiMusicText.Get("play", "PLAY"), 216, 207, 260, 40, () => _player.Toggle());
            _playLabel = _play.Label;
            _next = Control(panel, AishiMusicText.Get("next", "NEXT"), 488, 207, 180, 40, () => _player.Next());
            _shuffleLabel = Control(panel, AishiMusicText.Get("shuffleOff", "SHUFFLE: OFF"), 680, 207, 196, 40, () => _player.Shuffle = !_player.Shuffle).Label;
            Text(panel, AishiMusicText.Get("volume", "VOLUME"), 24, 263, 85, 26, 14, Muted);
            _volumeLabel = Text(panel, "", 818, 263, 58, 26, 14, Gold);
            var slider = Rect("Volume", panel, 110, 265, 694, 24);
            _volume = slider.gameObject.AddComponent<Slider>();
            _volume.minValue = 0;
            _volume.maxValue = 1;
            Fill(Rect("Rail", slider, 0, 10, 694, 4), new Color(.2f, .23f, .23f));
            var fillArea = Rect("FillArea", slider, 0, 10, 694, 4);
            var fill = Rect("Fill", fillArea, 0, 0, 0, 0);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            Fill(fill, Gold);
            _volume.fillRect = fill;
            var handle = Rect("Handle", slider, 0, 4, 8, 16);
            var handleImage = Fill(handle, Gold);
            _volume.handleRect = handle;
            _volume.targetGraphic = handleImage;
            _volume.SetValueWithoutNotify(_player.Volume);
            _volume.onValueChanged.AddListener(v => _player.Volume = v);
            Text(panel, AishiMusicText.Get("playlists", "PLAYLISTS"), 24, 303, 212, 25, 14, Gold);
            _libraryLabel = Text(panel, AishiMusicText.Get("tracks", "TRACKS"), 252, 303, 492, 25, 14, Gold);
            Control(panel, AishiMusicText.Get("refresh", "REFRESH"), 760, 299, 116, 30, () => _player.Reload());
            _playlistContent = Scroll(panel, "Playlists", 24, 341, 212, 210);
            _content = Scroll(panel, "Tracks", 252, 341, 624, 210);
            var inputRect = Rect("NewPlaylist", panel, 24, 565, 640, 36);
            Fill(inputRect, new Color(.10f, .12f, .12f));
            var viewport = Rect("TextViewport", inputRect, 10, 0, 620, 36);
            viewport.gameObject.AddComponent<RectMask2D>();
            var value = Text(viewport, "", 0, 0, 620, 36, 16, Gold);
            var placeholder = Text(viewport, AishiMusicText.Get("newPlaylist", "New playlist folder..."), 0, 0, 620, 36, 16, Muted);
            _newPlaylist = inputRect.gameObject.AddComponent<TMP_InputField>();
            _newPlaylist.textViewport = viewport;
            _newPlaylist.textComponent = value;
            _newPlaylist.placeholder = placeholder;
            _newPlaylist.characterLimit = 64;
            _newPlaylist.lineType = TMP_InputField.LineType.SingleLine;
            Control(panel, AishiMusicText.Get("createPlaylist", "CREATE PLAYLIST"), 676, 565, 200, 36, () =>
            {
                if (_player.CreatePlaylist(_newPlaylist.text))
                    _newPlaylist.text = "";
            });
            Text(panel, AishiMusicText.Get("formats", "LOCAL PLAYBACK  /  WAV · OGG · MP3  /  FOLDERS INSIDE MUSIC"), 24, 614, 852, 20, 12, Muted);
            Text(panel, _player.Shortcuts, 24, 643, 852, 20, 12, Gold);
            RefreshTracks();
        }

        internal void RefreshTracks()
        {
            foreach (Transform child in _content)
            {
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }

            foreach (Transform child in _playlistContent)
            {
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }

            _rows.Clear();
            _selected = -2;
            var playlists = _player.Playlists;
            for (int i = 0; i < playlists.Length; i++)
            {
                int index = i;
                var button = Button(_playlistContent, playlists[i], 0, i * 36, 212, 34, () => _player.ChoosePlaylist(index));
                button.GetComponentInChildren<TMP_Text>().alignment = TextAlignmentOptions.MidlineLeft;
                button.GetComponent<Image>().color = i == _player.SelectedPlaylist ? new Color(.23f, .25f, .20f) : new Color(.10f, .12f, .12f);
            }

            _playlistContent.sizeDelta = new Vector2(212, Math.Max(210, playlists.Length * 36));
            string[] tracks = _player.Tracks;
            _libraryLabel.text = _player.PlaylistName + "  /  " + tracks.Length + " " + AishiMusicText.Get("tracks", "TRACKS");
            for (int i = 0; i < tracks.Length; i++)
            {
                int index = i;
                var button = Button(_content, (i + 1).ToString("00") + "   " + Path.GetFileNameWithoutExtension(tracks[i]), 0, i * 38, 624, 36, () => _player.Select(index));
                button.GetComponentInChildren<TMP_Text>().alignment = TextAlignmentOptions.MidlineLeft;
                _rows.Add(button.GetComponent<Image>());
            }

            if (tracks.Length == 0)
            {
                Text(_content, AishiMusicText.Get("empty", "This playlist is empty"), 18, 24, 588, 30, 18, Gold);
                Text(_content, AishiMusicText.Get("addFiles", "Add files to its folder inside Music, then REFRESH."), 18, 57, 588, 30, 14, Muted);
            }

            _content.sizeDelta = new Vector2(624, Math.Max(210, tracks.Length * 38));
            _content.anchoredPosition = Vector2.zero;
            _nextRefresh = 0;
        }

        private void LateUpdate()
        {
            if (_panelRect == null)
                return;
            var viewport = ((RectTransform)transform).rect;
            float scale = Mathf.Min(1f, Mathf.Min((viewport.width - 32f) / 900f, (viewport.height - 32f) / 680f));
            _panelRect.localScale = Vector3.one * Mathf.Max(.1f, scale);
        }

        private static string Clock(float seconds)
        {
            int s = Math.Max(0, (int)seconds);
            return (s / 60) + ":" + (s % 60).ToString("00");
        }

        private void Update()
        {
            if (_player == null || Time.unscaledTime < _nextRefresh)
                return;
            _nextRefresh = Time.unscaledTime + .2f;
            _connection.text = _player.Connection;
            _status.text = _player.Status;
            int selected = _player.Selected;
            string[] tracks = _player.Tracks;
            _title.text = selected >= 0 && selected < tracks.Length ? Path.GetFileNameWithoutExtension(tracks[selected]) : AishiMusicText.Get("noTrack", "NO TRACK SELECTED");
            _play.interactable = _player.CanPlay;
            _previous.interactable = _player.CanPlay;
            _next.interactable = _player.CanPlay;
            _playLabel.text = _player.Paused ? AishiMusicText.Get("play", "PLAY") : AishiMusicText.Get("pause", "PAUSE");
            _shuffleLabel.text = _player.Shuffle ? AishiMusicText.Get("shuffleOn", "SHUFFLE: ON") : AishiMusicText.Get("shuffleOff", "SHUFFLE: OFF");
            _seek.interactable = _player.CanSeek;
            if (!_seek.Dragging || !_player.CanSeek || _selected != selected)
                _seek.SetValueWithoutNotify(_player.Duration > 0 ? Mathf.Clamp01(_player.Position / _player.Duration) : 0);
            _clock.text = Clock(_seek.Dragging && _player.CanSeek ? _seek.value * _player.Duration : _player.Position) + " / " + Clock(_player.Duration);
            _volume.SetValueWithoutNotify(_player.Volume);
            _volumeLabel.text = Mathf.RoundToInt(_player.Volume * 100) + "%";
            if (_selected == selected)
                return;
            _selected = selected;
            for (int i = 0; i < _rows.Count; i++)
                _rows[i].color = i == selected ? new Color(.23f, .25f, .20f) : new Color(.10f, .12f, .12f);
        }
    }

    public sealed class AishiMusicSeekSlider : Slider
    {
        internal bool Dragging { get; private set; }

        public override void OnPointerDown(PointerEventData eventData)
        {
            Dragging = IsActive() && IsInteractable() && eventData.button == PointerEventData.InputButton.Left;
            base.OnPointerDown(eventData);
        }

        public override void OnPointerUp(PointerEventData eventData)
        {
            base.OnPointerUp(eventData);
            if (eventData.button == PointerEventData.InputButton.Left)
                Dragging = false;
        }

        protected override void OnDisable()
        {
            Dragging = false;
            base.OnDisable();
        }
    }
}
