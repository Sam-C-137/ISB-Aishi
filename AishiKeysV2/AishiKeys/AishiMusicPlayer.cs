using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using AishiKeysPro;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using SPT.Reflection.Utils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace AishiKeys
{
    public sealed class AishiMusicPlayer : MonoBehaviour
    {
        internal static AishiMusicPlayer Instance;
        internal const string Cens = "a5f079ec80165ad68b7ac167", Comtac = "fb7fb28e480a5507bafcebd9";
        private ConfigEntry<KeyboardShortcut> _pauseKey, _windowKey;
        private ConfigEntry<float> _volume;
        private ConfigEntry<bool> _next, _shuffle;
        private AudioSource _source;
        private AishiHeadsetAudio _headsetTone;
        private Player _player;
        private GameWorld _world;
        private Profile _menuProfile;
        private string _directory, _musicRoot;
        private string[] _playlistPaths = new string[0];
        private int _playlistIndex;
        private readonly AishiMusicOrder _order = new AishiMusicOrder();
        private string _status = AishiMusicText.Get("equip", "Equip an Aishi music headset to listen.");
        private string[] _tracks = new string[0];
        private int _index = -1, _generation;
        private bool _eligible, _paused = true, _window, _loading, _started;
        private float _nextWorldCheck;
        private UnityWebRequest _request;
        private Coroutine _load;
        private AishiMusicPanel _panel;
        private GameObject _inputShield;
        internal static bool Supported(string id) => id == Cens || id == Comtac;
        internal static void Enable(AishiKeysMod plugin)
        {
            if (plugin.NetworkBridge?.IsHeadless == true)
                return;
            var player = plugin.gameObject.AddComponent<AishiMusicPlayer>();
            Instance = player;
            player._musicRoot = player._directory = Path.Combine(Path.GetDirectoryName(plugin.Info.Location), "Music");
            Directory.CreateDirectory(player._directory);
            player._pauseKey = plugin.Config.Bind("Music", "Pause or resume", new KeyboardShortcut(KeyCode.Pause), "Pause/resume music. Only the two equipped Aishi music headsets can play.");
            player._windowKey = plugin.Config.Bind("Music", "Open player", new KeyboardShortcut(KeyCode.ScrollLock), "Toggle the music selector while inventory or a menu is open. Also available in the headset inspection window.");
            player._volume = plugin.Config.Bind("Music", "Volume", .30f, new ConfigDescription("Personal music volume; game and headset audio are unchanged.", new AcceptableValueRange<float>(0, 1)));
            player._next = plugin.Config.Bind("Music", "Play next track", true, "Continue with the next installed song when a track finishes.");
            player._shuffle = plugin.Config.Bind("Music", "Shuffle playlist", false, "Shuffle the selected playlist without repeating the remaining tracks. Also available in the player.");
            var audioObject = new GameObject("AishiLocalMusicAudio");
            audioObject.transform.SetParent(player.transform, false);
            player._source = audioObject.AddComponent<AudioSource>();
            player._headsetTone = audioObject.AddComponent<AishiHeadsetAudio>();
            player._source.playOnAwake = false;
            player._source.spatialBlend = 0;
            player._source.loop = false;
            player._source.priority = 200;
            player._source.ignoreListenerPause = true;
            player.RefreshPlaylists();
            player.RefreshTracks();
            var harmony = new Harmony("com.samc137.aishi.music-inspection");
            harmony.Patch(AccessTools.Method(typeof(ItemSpecificationPanel), "Show"), postfix: new HarmonyMethod(typeof(AishiMusicPlayer), nameof(Inspection)));
            harmony.Patch(AccessTools.Method(typeof(ItemSpecificationPanel), "Close"), postfix: new HarmonyMethod(typeof(AishiMusicPlayer), nameof(CloseInspection)));
        }

        private static readonly FieldInfo InspectedItem = typeof(ItemSpecificationPanel).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single(f => f.FieldType == typeof(Item));
        private static void Inspection(ItemSpecificationPanel __instance)
        {
            var item = InspectedItem.GetValue(__instance) as Item;
            var holder = __instance.transform.Find("AishiMusicButton");
            if (holder != null)
                holder.gameObject.SetActive(Supported(item?.TemplateId));
            if (!Supported(item?.TemplateId) || holder != null)
                return;
            var go = new GameObject("AishiMusicButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(__instance.transform, false);
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(12, -40);
            rect.sizeDelta = new Vector2(170, 30);
            go.GetComponent<Image>().color = new Color(.08f, .11f, .12f, .96f);
            var label = new GameObject("Label", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI));
            label.transform.SetParent(go.transform, false);
            var lr = (RectTransform)label.transform;
            lr.anchorMin = Vector2.zero;
            lr.anchorMax = Vector2.one;
            lr.offsetMin = lr.offsetMax = Vector2.zero;
            var text = label.GetComponent<TMPro.TextMeshProUGUI>();
            var sample = __instance.GetComponentInChildren<TMPro.TMP_Text>(true);
            if (sample != null)
                text.font = sample.font;
            text.text = AishiMusicText.Get("player", "MUSIC PLAYER");
            text.fontSize = 16;
            text.alignment = TMPro.TextAlignmentOptions.Center;
            text.raycastTarget = false;
            go.GetComponent<Button>().onClick.AddListener(() =>
            {
                if (Instance != null)
                    Instance.SetWindow(!Instance._window);
            });
        }

        private static void CloseInspection(ItemSpecificationPanel __instance)
        {
            var holder = __instance.transform.Find("AishiMusicButton");
            if (holder != null)
                holder.gameObject.SetActive(false);
        }

        private bool MenuOpen => _player == null || _player.IsInventoryOpened || (Cursor.visible && Cursor.lockState != CursorLockMode.Locked);

        private void SetWindow(bool open)
        {
            _window = open;
            if (open && _panel == null)
            {
                _panel = AishiMusicPanel.Create(this);
                _inputShield = _panel.gameObject;
            }

            if (_inputShield != null)
                _inputShield.SetActive(open);
            if (open)
                _panel.RefreshTracks();
        }

        internal string[] Tracks => _tracks;
        internal int Selected => _index;
        internal bool CanPlay => _eligible && !_loading && _tracks.Length != 0;
        internal bool Paused => _paused;
        internal string Status => _status;
        internal string Connection => _eligible ? AishiMusicText.Get("connected", "HEADSET CONNECTED") : AishiMusicText.Get("disconnected", "HEADSET NOT EQUIPPED");
        internal float Volume { get => _volume.Value; set => _volume.Value = Mathf.Clamp01(value); }
        internal float Position => _source?.clip != null ? _source.time : 0;
        internal float Duration => _source?.clip != null ? _source.clip.length : 0;
        internal bool CanSeek => _eligible && !_loading && _source?.clip != null && _source.clip.samples > 0;

        internal void Seek(float position)
        {
            if (!CanSeek || float.IsNaN(position) || float.IsInfinity(position))
                return;
            _source.timeSamples = (int)Math.Round((_source.clip.samples - 1) * (double)Mathf.Clamp01(position));
            _headsetTone?.ResetFilter();
        }

        internal string Shortcuts => string.Format(AishiMusicText.Get("shortcuts", "PAUSE / RESUME  {0}     PLAYER  {1}"), _pauseKey.Value, _windowKey.Value);

        internal void ClosePlayer() => SetWindow(false);
        internal void Previous()
        {
            if (CanPlay)
                PlaySelection(_order.Previous(Shuffle));
        }

        internal void Next()
        {
            if (CanPlay)
                PlaySelection(_order.Next(Shuffle));
        }

        internal bool Shuffle
        {
            get => _shuffle.Value;
            set
            {
                _shuffle.Value = value;
                _order.Reset(_tracks.Length, _index);
            }
        }

        internal string[] Playlists => _playlistPaths.Select((p, i) => i == 0 ? AishiMusicText.Get("root", "Music (root)") : Path.GetFileName(p)).ToArray();
        internal int SelectedPlaylist => _playlistIndex;
        internal string PlaylistName => _playlistIndex == 0 ? AishiMusicText.Get("root", "Music (root)") : Path.GetFileName(_directory);

        private void RefreshPlaylists()
        {
            try
            {
                _playlistPaths = AishiMusicLibrary.Folders(_musicRoot);
                int index = Array.FindIndex(_playlistPaths, p => string.Equals(p, _directory, StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                {
                    StopPlayback();
                    _directory = _musicRoot;
                    _index = -1;
                }

                _playlistIndex = Math.Max(0, index);
            }
            catch (Exception ex)
            {
                _status = AishiMusicText.Get("readPlaylists", "Cannot read playlists: ") + ex.Message;
            }
        }

        internal void ChoosePlaylist(int index)
        {
            if (index < 0 || index >= _playlistPaths.Length || index == _playlistIndex)
                return;
            if (_started && !_paused)
                Notify(AishiMusicText.Get("notifyPaused", "Music paused"));
            StopPlayback();
            _directory = _playlistPaths[index];
            _playlistIndex = index;
            _index = -1;
            RefreshTracks();
            _status = _tracks.Length == 0 ? AishiMusicText.Get("emptyPath", "Empty playlist. Add music to Music/") + PlaylistName : AishiMusicText.Get("selectedPlaylist", "Playlist selected. Choose a track or press Play.");
            _panel?.RefreshTracks();
        }

        internal bool CreatePlaylist(string name)
        {
            try
            {
                string path = AishiMusicLibrary.Create(_musicRoot, name);
                RefreshPlaylists();
                ChoosePlaylist(Array.FindIndex(_playlistPaths, p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)));
                return true;
            }
            catch (Exception ex)
            {
                _status = AishiMusicText.Get("createError", "Cannot create playlist: ") + ex.Message;
                return false;
            }
        }

        internal void Reload()
        {
            RefreshPlaylists();
            RefreshTracks();
            _panel?.RefreshTracks();
        }

        private static bool Typing()
        {
            var selected = EventSystem.current?.currentSelectedGameObject;
            return selected != null && (selected.GetComponent<InputField>() != null || selected.GetComponent<TMPro.TMP_InputField>() != null);
        }

        private void Update()
        {
            if (AishiKeysMod.Instance?.NetworkBridge?.IsHeadless == true)
            {
                StopPlayback();
                if (_source != null)
                    _source.gameObject.SetActive(false);
                if (_panel != null)
                    _panel.gameObject.SetActive(false);
                enabled = false;
                return;
            }

            if (_source == null)
                return;
            var world = Singleton<GameWorld>.Instance;
            if (_world != world || Time.unscaledTime >= _nextWorldCheck)
            {
                if (_world != world)
                    StopPlayback();
                _world = world;
                var local = world?.MainPlayer;
                if (local == null && world != null)
                    local = world.RegisteredPlayers.OfType<HideoutPlayer>().FirstOrDefault();
                if (_player != local)
                    StopPlayback();
                _player = local;
                _nextWorldCheck = Time.unscaledTime + .5f;
            }

            var hideout = _player as HideoutPlayer;
            var equipment = hideout != null ? hideout.OriginalInventory?.Inventory?.Equipment : _player?.Equipment;
            var menuProfile = world == null ? ClientAppUtils.GetClientApp()?.Session?.Profile : null;
            if (!ReferenceEquals(_menuProfile, menuProfile))
                StopPlayback();
            _menuProfile = menuProfile;
            if (_menuProfile != null)
                equipment = _menuProfile.Inventory?.Equipment;
            bool localContext = _menuProfile != null || (_player != null && (_player.IsYourPlayer || hideout != null) && _player.HealthController != null && _player.HealthController.IsAlive);
            bool eligible = localContext && Supported(equipment?.GetSlot(EquipmentSlot.Earpiece)?.ContainedItem?.TemplateId);
            if (_eligible && !eligible)
            {
                StopPlayback();
                _status = AishiMusicText.Get("unequipped", "Music stopped: equip an Aishi music headset.");
            }

            if (!_eligible && eligible)
                _status = AishiMusicText.Get("ready", "Headset connected. Press Play to listen.");
            _eligible = eligible;
            _source.volume = _volume.Value;
            if (!Typing())
            {
                if (_pauseKey.Value.IsDown() && _eligible && !_loading)
                    Toggle();
                if (_windowKey.Value.IsDown() && MenuOpen)
                    SetWindow(!_window);
            }

            if (!MenuOpen && _window)
                SetWindow(false);
            if (_started && !_paused && !_loading && !_source.isPlaying && (!AudioListener.pause || _source.ignoreListenerPause) && Application.isFocused)
            {
                _started = false;
                if (_next.Value && _tracks.Length > 1)
                    Next();
                else
                {
                    _paused = true;
                    _status = AishiMusicText.Get("finished", "Track finished.");
                }
            }
        }

        private void RefreshTracks()
        {
            string selected = _index >= 0 && _index < _tracks.Length ? _tracks[_index] : null;
            try
            {
                _tracks = Directory.EnumerateFiles(_directory).Where(p => MusicFiles.SupportedExtension(p) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) == 0).OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase).Take(500).ToArray();
                _index = Array.IndexOf(_tracks, selected);
                if (selected != null && _index < 0)
                    StopPlayback();
                _order.Reset(_tracks.Length, _index);
                if (_tracks.Length == 0)
                    _status = AishiMusicText.Get("addFormats", "Add WAV, OGG or MP3 files to this playlist folder, then Refresh.");
            }
            catch (Exception ex)
            {
                StopPlayback();
                _tracks = new string[0];
                _index = -1;
                _order.Reset(0);
                _status = AishiMusicText.Get("readMusic", "Cannot read Music folder: ") + ex.Message;
            }
        }

        internal void Toggle()
        {
            if (!_eligible)
                return;
            if (_source.clip == null)
            {
                if (_tracks.Length != 0)
                {
                    if (_index < 0)
                        Next();
                    else
                        PlaySelection(_index);
                }

                return;
            }

            if (_paused)
            {
                if (_started)
                    _source.UnPause();
                else
                {
                    int position = _source.timeSamples;
                    _source.Play();
                    _source.timeSamples = position;
                }
                _started = true;
                _paused = false;
                _status = AishiMusicText.Get("playing", "Playing");
                Notify(AishiMusicText.Get("notifyResumed", "Music resumed"));
            }
            else
            {
                _source.Pause();
                _paused = true;
                _status = AishiMusicText.Get("paused", "Paused");
                Notify(AishiMusicText.Get("notifyPaused", "Music paused"));
            }
        }

        internal void Select(int index)
        {
            if (index < 0 || index >= _tracks.Length)
                return;
            _order.Select(index);
            PlaySelection(index);
        }

        private void PlaySelection(int index)
        {
            if (index < 0 || index >= _tracks.Length)
                return;
            StopPlayback();
            _index = index;
            if (!_eligible)
            {
                _status = AishiMusicText.Get("selected", "Selected. Equip an Aishi music headset, then press Play.");
                return;
            }

            _load = StartCoroutine(LoadTrack(_tracks[index], _generation));
        }

        private IEnumerator LoadTrack(string file, int generation)
        {
            if (!File.Exists(file) || new FileInfo(file).Length > 100 * 1024 * 1024)
            {
                _status = AishiMusicText.Get("missing", "Missing file or file larger than 100 MB.");
                yield break;
            }

            AudioType type = Path.GetExtension(file).Equals(".ogg", StringComparison.OrdinalIgnoreCase) ? AudioType.OGGVORBIS : Path.GetExtension(file).Equals(".wav", StringComparison.OrdinalIgnoreCase) ? AudioType.WAV : AudioType.MPEG;
            _loading = true;
            _status = AishiMusicText.Get("loading", "Loading ") + Path.GetFileNameWithoutExtension(file);
            using (var request = UnityWebRequestMultimedia.GetAudioClip(new Uri(file).AbsoluteUri, type))
            {
                _request = request;
                yield return request.SendWebRequest();
                if (generation == _generation && _eligible)
                {
                    if (request.result != UnityWebRequest.Result.Success)
                        _status = AishiMusicText.Get("playError", "Cannot play this file: ") + request.error;
                    else
                    {
                        var clip = DownloadHandlerAudioClip.GetContent(request);
                        if (clip != null)
                        {
                            _headsetTone?.ResetFilter();
                            _source.clip = clip;
                            _source.Play();
                            _paused = false;
                            _started = true;
                            _status = AishiMusicText.Get("playing", "Playing");
                            Notify(AishiMusicText.Get("notifyPlaying", "Now playing: ") + TrackLabel(file));
                        }
                    }
                }

                _request = null;
            }

            _loading = false;
            _load = null;
        }

        private void StopPlayback()
        {
            _generation++;
            if (_request != null)
            {
                _request.Abort();
                _request.Dispose();
                _request = null;
            }

            if (_load != null)
            {
                StopCoroutine(_load);
                _load = null;
            }

            if (_source != null)
            {
                _source.Stop();
                var clip = _source.clip;
                _source.clip = null;
                if (clip != null)
                    Destroy(clip);
            }

            _loading = false;
            _paused = true;
            _started = false;
            _status = AishiMusicText.Get("stopped", "Stopped");
        }

        private void Notify(string message)
        {
            if (!_eligible)
                return;
            try
            {
                EFT.Communications.NotificationManager.DisplayMessageNotification(message, EFT.Communications.ENotificationDurationType.Default);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("Aishi music notice: " + ex.Message);
            }
        }

        private static string TrackLabel(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path).Replace("<", "").Replace(">", "").Replace("\r", " ").Replace("\n", " ");
            return name.Length > 100 ? name.Substring(0, 100) : name;
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && _source != null && !_paused)
            {
                _source.Pause();
                _paused = true;
                _status = AishiMusicText.Get("paused", "Paused");
                Notify(AishiMusicText.Get("notifyPaused", "Music paused"));
            }
        }

        private void OnDisable()
        {
            StopPlayback();
            SetWindow(false);
        }

        private void OnDestroy()
        {
            StopPlayback();
            if (_inputShield != null)
                Destroy(_inputShield);
            if (_source != null)
                Destroy(_source.gameObject);
            if (Instance == this)
                Instance = null;
        }
    }

    internal static class MusicFiles
    {
        internal static bool SupportedExtension(string path)
        {
            string ext = Path.GetExtension(path);
            return ext.Equals(".wav", StringComparison.OrdinalIgnoreCase) || ext.Equals(".ogg", StringComparison.OrdinalIgnoreCase) || ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase);
        }
    }
}
