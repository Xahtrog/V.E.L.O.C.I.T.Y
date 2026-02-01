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

namespace taimi.Velocity
{
    public enum WindowPositionType { Center, Left, Right }

    [Export(typeof(Blish_HUD.Modules.Module))]
    public class VelocityModule : Blish_HUD.Modules.Module
    {
        private static readonly Logger Logger = Logger.GetLogger<VelocityModule>();

        private CornerIcon _cornerIcon;
        private Panel _mainWindow;

        // Custom Controls
        private Label _titleLabel;
        private StandardButton _closeButton;
        private TextBox _searchBox;
        private FlowPanel _resultsPanel;

        // Dragging & Animation State
        private bool _isDragging = false;
        private Point _dragStart = Point.Zero;

        // Animation Variables
        private float _targetOpacity = 0f;
        private float _currentOpacity = 0f;
        private bool _isOpen = false;

        private SettingEntry<KeyBinding> _toggleHotkey;
        private SettingEntry<WindowPositionType> _windowPositionSetting;
        private List<WaypointData> _waypointCache = new List<WaypointData>();

        // Layout Constants
        private const int WINDOW_WIDTH = 340;
        private const int HEADER_HEIGHT = 110;
        private const int ITEM_HEIGHT = 35;
        private const int MAX_WINDOW_HEIGHT = 500;

        [ImportingConstructor]
        public VelocityModule([Import("ModuleParameters")] ModuleParameters moduleParameters) : base(moduleParameters) { }

        protected override void DefineSettings(Blish_HUD.Settings.SettingCollection settings)
        {
            _toggleHotkey = settings.DefineSetting(
                "ToggleKey",
                new KeyBinding(ModifierKeys.Ctrl | ModifierKeys.Shift, Keys.F),
                () => "Search Hotkey",
                () => "Press this key to open the Waypoint Search bar."
            );

            _windowPositionSetting = settings.DefineSetting(
                "WindowPosition",
                WindowPositionType.Center,
                () => "Default Open Position",
                () => "Where should the search bar appear when opened?"
            );

            _toggleHotkey.Value.Enabled = true;
            _toggleHotkey.Value.Activated += OnHotkeyPressed;
        }

        private void OnHotkeyPressed(object sender, EventArgs e)
        {
            if (_mainWindow != null)
            {
                ToggleWindow();
            }
        }

        private void ToggleWindow()
        {
            _isOpen = !_isOpen;

            if (_isOpen)
            {
                // Open Logic
                _mainWindow.Visible = true;
                _targetOpacity = 1.0f;
                ApplyWindowPosition();
                _searchBox.Focused = true;
            }
            else
            {
                // Close Logic (Fade out)
                _targetOpacity = 0.0f;
            }
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
            try
            {
                var client = GameService.Gw2WebApi.AnonymousConnection.Client.V2;
                var floor = await client.Continents[1].Floors.GetAsync(1);
                foreach (var r in floor.Regions.Values) foreach (var m in r.Maps.Values) foreach (var p in m.PointsOfInterest.Values)
                            if (p.Type == PoiType.Waypoint) _waypointCache.Add(new WaypointData { Name = p.Name, Id = p.Id, ChatLink = p.ChatLink });
            }
            catch (Exception ex) { Logger.Error(ex, "Failed to load waypoints."); }
        }

        protected override void OnModuleLoaded(EventArgs e)
        {
            var icon = GameService.Content.DatAssetCache.GetTextureFromAssetId(156014);

            // 1. Create Panel (Standard Blish HUD Panel)
            _mainWindow = new Panel()
            {
                Parent = GameService.Graphics.SpriteScreen,
                Size = new Point(WINDOW_WIDTH, HEADER_HEIGHT),
                // Sleek Dark Background
                BackgroundColor = Color.FromNonPremultiplied(10, 10, 15, 240),
                ShowBorder = true,
                Visible = false,
                Opacity = 0f,
                ZIndex = 40
            };

            // 2. Title Bar
            _titleLabel = new Label()
            {
                Parent = _mainWindow,
                Text = "V.E.L.O.C.I.T.Y.",
                Location = new Point(0, 15),
                Width = WINDOW_WIDTH,
                Height = 40,
                HorizontalAlignment = HorizontalAlignment.Center,
                Font = GameService.Content.DefaultFont32,
                TextColor = Color.LightBlue,
                StrokeText = true
            };

            // 3. Close Button
            _closeButton = new StandardButton()
            {
                Parent = _mainWindow,
                Text = "X",
                Width = 25,
                Height = 25,
                Location = new Point(WINDOW_WIDTH - 35, 5),
                BackgroundColor = Color.Maroon
            };
            _closeButton.Click += delegate { ToggleWindow(); };

            // 4. Dragging Logic (FIXED)
            _mainWindow.LeftMouseButtonPressed += delegate (object sender, MouseEventArgs args) {
                // Calculate where the mouse is relative to the window top
                int relativeY = GameService.Input.Mouse.Position.Y - _mainWindow.Location.Y;

                // Only allow dragging if clicking the top 60 pixels (The "Title Bar")
                // This protects the search bar and results/scrollbar from accidental dragging
                if (relativeY < 60)
                {
                    _isDragging = true;
                    _dragStart = GameService.Input.Mouse.Position - _mainWindow.Location;
                }
            };
            _mainWindow.LeftMouseButtonReleased += delegate { _isDragging = false; };

            // 5. Search Bar
            _searchBox = new TextBox()
            {
                Parent = _mainWindow,
                PlaceholderText = "Search Waypoints...",
                Width = 300,
                // Left aligned at 20px
                Location = new Point(20, 65)
            };
            _searchBox.TextChanged += OnSearchTextChanged;

            // 6. Results Panel
            _resultsPanel = new FlowPanel()
            {
                Parent = _mainWindow,
                // Centered Alignment
                Location = new Point(30, _searchBox.Bottom + 10),
                Width = 300,
                Height = 0,
                FlowDirection = ControlFlowDirection.SingleTopToBottom,
                CanScroll = true,
                ShowBorder = false,
                BackgroundColor = Color.Transparent,
                Visible = false
            };

            _cornerIcon = new CornerIcon() { IconName = "Search Waypoints", Icon = icon, Priority = 5 };
            _cornerIcon.Click += delegate { ToggleWindow(); };

            base.OnModuleLoaded(e);
        }

        // 7. ANIMATION LOOP
        protected override void Update(GameTime gameTime)
        {
            if (_mainWindow != null && _mainWindow.Visible && _isDragging)
            {
                _mainWindow.Location = GameService.Input.Mouse.Position - _dragStart;
            }

            if (_mainWindow != null)
            {
                // Nice slow cinematic fade (0.05f)
                float speed = 0.05f;

                _currentOpacity = MathHelper.Lerp(_currentOpacity, _targetOpacity, speed);
                _mainWindow.Opacity = _currentOpacity;

                if (_currentOpacity < 0.01f && !_isOpen)
                {
                    _mainWindow.Visible = false;
                }
            }
        }

        private void OnSearchTextChanged(object sender, EventArgs e)
        {
            string query = _searchBox.Text.ToLower();
            _resultsPanel.ClearChildren();

            if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            {
                _resultsPanel.Visible = false;
                _mainWindow.Height = HEADER_HEIGHT;
                return;
            }

            var matches = _waypointCache
                .Where(wp => wp.Name != null && wp.Name.ToLower().Contains(query))
                .Take(100)
                .ToList();

            if (matches.Count > 0)
            {
                foreach (var wp in matches)
                {
                    var wpButton = new StandardButton()
                    {
                        Text = wp.Name,
                        Width = 280, // Matches perfectly with X=30
                        Parent = _resultsPanel,
                        BackgroundColor = Color.FromNonPremultiplied(40, 40, 40, 200)
                    };
                    wpButton.Click += delegate { CopyWaypointToClipboard(wp); };
                }

                int contentHeight = matches.Count * ITEM_HEIGHT;
                int newResultsHeight = Math.Min(contentHeight + 10, MAX_WINDOW_HEIGHT - HEADER_HEIGHT);
                int newWindowHeight = HEADER_HEIGHT + newResultsHeight + 10;

                _resultsPanel.Height = newResultsHeight;
                _resultsPanel.Visible = true;
                _mainWindow.Height = newWindowHeight;
            }
            else
            {
                _resultsPanel.Visible = false;
                _mainWindow.Height = HEADER_HEIGHT;
            }
        }

        public void CopyWaypointToClipboard(WaypointData wp)
        {
            if (wp == null) return;
            System.Windows.Forms.Clipboard.SetText(wp.ChatLink);
            ScreenNotification.ShowNotification($"Linked: {wp.Name}", ScreenNotification.NotificationType.Info);
            ToggleWindow();
        }

        protected override void Unload()
        {
            if (_toggleHotkey != null) _toggleHotkey.Value.Activated -= OnHotkeyPressed;
            _cornerIcon?.Dispose();
            _mainWindow?.Dispose();
            _waypointCache.Clear();
        }
    }

    public class WaypointData { public string Name { get; set; } public int Id { get; set; } public string ChatLink { get; set; } }
}