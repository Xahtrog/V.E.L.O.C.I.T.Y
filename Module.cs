using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading.Tasks;
using System.Linq; 
using Blish_HUD;
using Blish_HUD.Modules;
using Blish_HUD.Controls;
using Blish_HUD.Settings;
using Blish_HUD.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Gw2Sharp.WebApi.V2.Models;

using Rectangle = Microsoft.Xna.Framework.Rectangle;
using Color = Microsoft.Xna.Framework.Color;
using Point = Microsoft.Xna.Framework.Point;

namespace Velocity
{
    public enum WindowPositionType { Center, Left, Right }

    [Export(typeof(Blish_HUD.Modules.Module))]
    public class WaypointModule : Blish_HUD.Modules.Module
    {
        private static readonly Logger Logger = Logger.GetLogger<WaypointModule>();

        private CornerIcon _cornerIcon;
        private Panel _mainWindow;
        private Label _titleLabel;
        private StandardButton _closeButton;
        private TextBox _searchBox;
        private FlowPanel _resultsPanel;
        private Label _loadingLabel; // New loading indicator

        private bool _isDragging = false;
        private Point _dragStart = Point.Zero;
        private float _targetOpacity = 0f;
        private float _currentOpacity = 0f;
        private bool _isOpen = false;
        private bool _isDataLoaded = false;

        private SettingEntry<KeyBinding> _toggleHotkey;
        private SettingEntry<WindowPositionType> _windowPositionSetting; 
        private List<WaypointData> _waypointCache = new List<WaypointData>();

        private const int WINDOW_WIDTH = 340;
        private const int HEADER_HEIGHT = 110; 
        private const int ITEM_HEIGHT = 35;    
        private const int MAX_WINDOW_HEIGHT = 500;

        [ImportingConstructor]
        public WaypointModule([Import("ModuleParameters")] ModuleParameters moduleParameters) : base(moduleParameters) { }

        protected override void DefineSettings(Blish_HUD.Settings.SettingCollection settings)
        {
            _toggleHotkey = settings.DefineSetting("ToggleKey", new KeyBinding(ModifierKeys.Ctrl | ModifierKeys.Shift, Keys.F), () => "Search Hotkey", () => "Press to open search.");
            _windowPositionSetting = settings.DefineSetting("WindowPosition", WindowPositionType.Center, () => "Default Position", () => "Where the bar appears.");
            _toggleHotkey.Value.Enabled = true;
            _toggleHotkey.Value.Activated += OnHotkeyPressed;
        }

        private void OnHotkeyPressed(object sender, EventArgs e) { if (_mainWindow != null) ToggleWindow(); }

        private void ToggleWindow()
        {
            _isOpen = !_isOpen;
            if (_isOpen)
            {
                _mainWindow.Visible = true;
                _targetOpacity = 1.0f;
                ApplyWindowPosition();
                _searchBox.Focused = true;
            }
            else { _targetOpacity = 0.0f; }
        }

        private void ApplyWindowPosition()
        {
            var screen = GameService.Graphics.SpriteScreen;
            int x = 0;
            int y = (screen.Height / 2) - (_mainWindow.Height / 2); 
            switch (_windowPositionSetting.Value)
            {
                case WindowPositionType.Left: x = 100; break;
                case WindowPositionType.Right: x = screen.Width - _mainWindow.Width - 100; break;
                default: x = (screen.Width / 2) - (_mainWindow.Width / 2); break;
            }
            _mainWindow.Location = new Point(x, y);
        }

        protected override async Task LoadAsync()
        {
            // The expansion fix: Searching Continent 1 (Tyria) and 2 (Mists/Expansions)
            try {
                var client = GameService.Gw2WebApi.AnonymousConnection.Client.V2;
                int[] continents = { 1, 2 };

                foreach (int contId in continents) {
                    var continent = await client.Continents.GetAsync(contId);
                    foreach (int floorId in continent.Floors) {
                        var floor = await client.Continents[contId].Floors.GetAsync(floorId);
                        foreach (var region in floor.Regions.Values) {
                            foreach (var map in region.Maps.Values) {
                                foreach (var poi in map.PointsOfInterest.Values) {
                                    if (poi.Type == PoiType.Waypoint && !_waypointCache.Any(w => w.Id == poi.Id)) {
                                        _waypointCache.Add(new WaypointData { Name = poi.Name, Id = poi.Id, ChatLink = poi.ChatLink });
                                    }
                                }
                            }
                        }
                    }
                }
                _isDataLoaded = true;
                if (_loadingLabel != null) _loadingLabel.Visible = false;
            } catch (Exception ex) { Logger.Error(ex, "Failed expansion waypoint load."); }
        }

        protected override void OnModuleLoaded(EventArgs e)
        {
            var icon = GameService.Content.DatAssetCache.GetTextureFromAssetId(156014);

            _mainWindow = new Panel() {
                Parent = GameService.Graphics.SpriteScreen,
                Size = new Point(WINDOW_WIDTH, HEADER_HEIGHT),
                BackgroundColor = Color.FromNonPremultiplied(10, 10, 15, 240), 
                ShowBorder = true, Visible = false, Opacity = 0f, ZIndex = 40
            };

            _titleLabel = new Label() { Parent = _mainWindow, Text = "V.E.L.O.C.I.T.Y.", Location = new Point(0, 15), Width = WINDOW_WIDTH, Height = 40, HorizontalAlignment = HorizontalAlignment.Center, Font = GameService.Content.DefaultFont32, TextColor = Color.LightBlue, StrokeText = true };
            _closeButton = new StandardButton() { Parent = _mainWindow, Text = "X", Width = 25, Height = 25, Location = new Point(WINDOW_WIDTH - 35, 5), BackgroundColor = Color.Maroon };
            _closeButton.Click += delegate { ToggleWindow(); };

            _mainWindow.LeftMouseButtonPressed += delegate(object sender, MouseEventArgs args) {
                if ((GameService.Input.Mouse.Position.Y - _mainWindow.Location.Y) < 60) {
                    _isDragging = true;
                    _dragStart = GameService.Input.Mouse.Position - _mainWindow.Location;
                }
            };
            _mainWindow.LeftMouseButtonReleased += delegate { _isDragging = false; };

            _searchBox = new TextBox() { Parent = _mainWindow, PlaceholderText = "Search Waypoints...", Width = 300, Location = new Point(20, 65) };
            _searchBox.TextChanged += OnSearchTextChanged;

            _loadingLabel = new Label() {
                Parent = _mainWindow, Text = "Syncing Expansions...", Location = new Point(20, 95), Width = 300, 
                HorizontalAlignment = HorizontalAlignment.Center, TextColor = Color.Orange, Visible = !_isDataLoaded
            };

            _resultsPanel = new FlowPanel() { Parent = _mainWindow, Location = new Point(30, _searchBox.Bottom + 10), Width = 300, Height = 0, FlowDirection = ControlFlowDirection.SingleTopToBottom, CanScroll = true, ShowBorder = false, Visible = false };

            _cornerIcon = new CornerIcon() { IconName = "Search Waypoints", Icon = icon, Priority = 5 };
            _cornerIcon.Click += delegate { ToggleWindow(); };
            base.OnModuleLoaded(e);
        }

        protected override void Update(GameTime gameTime)
        {
            if (_mainWindow != null && _mainWindow.Visible && _isDragging) _mainWindow.Location = GameService.Input.Mouse.Position - _dragStart;
            if (_mainWindow != null) {
                _currentOpacity = MathHelper.Lerp(_currentOpacity, _targetOpacity, 0.05f);
                _mainWindow.Opacity = _currentOpacity;
                if (_currentOpacity < 0.01f && !_isOpen) _mainWindow.Visible = false;
            }
        }

        private void OnSearchTextChanged(object sender, EventArgs e)
        {
            string query = _searchBox.Text.ToLower();
            _resultsPanel.ClearChildren();
            if (string.IsNullOrWhiteSpace(query) || query.Length < 2) { _resultsPanel.Visible = false; _mainWindow.Height = HEADER_HEIGHT; return; }

            var matches = _waypointCache.Where(wp => wp.Name != null && wp.Name.ToLower().Contains(query)).Take(100).ToList();
            if (matches.Count > 0) {
                foreach (var wp in matches) {
                    var wpButton = new StandardButton() { Text = wp.Name, Width = 280, Parent = _resultsPanel, BackgroundColor = Color.FromNonPremultiplied(40, 40, 40, 200) };
                    wpButton.Click += delegate { CopyWaypointToClipboard(wp); };
                }
                int newResultsHeight = Math.Min((matches.Count * ITEM_HEIGHT) + 10, MAX_WINDOW_HEIGHT - HEADER_HEIGHT);
                _resultsPanel.Height = newResultsHeight; _resultsPanel.Visible = true;
                _mainWindow.Height = HEADER_HEIGHT + newResultsHeight + 10;
            } else { _resultsPanel.Visible = false; _mainWindow.Height = HEADER_HEIGHT; }
        }

        public void CopyWaypointToClipboard(WaypointData wp) {
            if (wp == null) return;
            System.Windows.Forms.Clipboard.SetText(wp.ChatLink);
            ScreenNotification.ShowNotification($"Linked: {wp.Name}", ScreenNotification.NotificationType.Info);
            ToggleWindow(); 
        }

        protected override void Unload() { _toggleHotkey.Value.Activated -= OnHotkeyPressed; _cornerIcon?.Dispose(); _mainWindow?.Dispose(); _waypointCache.Clear(); }
    }

    public class WaypointData { public string Name { get; set; } public int Id { get; set; } public string ChatLink { get; set; } }
}
