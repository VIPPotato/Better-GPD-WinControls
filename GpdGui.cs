using System;
using System.Windows.Forms;
using System.Drawing;
using System.Collections.Generic;
using System.IO;
using GpdControl;

namespace GpdGui
{
    public class MainForm : Form
    {
        // private ListBox keyList;
        // private ComboBox valueCombo;
        private Button applyButton;
        private Button reloadButton;
        private Label statusLabel;
        private Config currentConfig;
        private GpdDevice device;

        public MainForm()
        {
            this.Text = "Better GPD WinControl";
            this.Size = new Size(600, 500);
            this.StartPosition = FormStartPosition.CenterScreen;

            // Layout
            TableLayoutPanel mainLayout = new TableLayoutPanel();
            mainLayout.Dock = DockStyle.Fill;
            mainLayout.RowCount = 3;
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); // Top buttons
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Content
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); // Status
            this.Controls.Add(mainLayout);

            // Top Buttons
            FlowLayoutPanel buttonPanel = new FlowLayoutPanel();
            buttonPanel.Dock = DockStyle.Fill;
            reloadButton = new Button();
            reloadButton.Text = "Reload from Device";
            reloadButton.AutoSize = true;
            reloadButton.Click += new EventHandler(ReloadButton_Click);
            
            applyButton = new Button();
            applyButton.Text = "Apply Changes";
            applyButton.AutoSize = true;
            applyButton.Click += new EventHandler(ApplyButton_Click);
            
            Button resetButton = new Button();
            resetButton.Text = "Reset to Defaults";
            resetButton.AutoSize = true;
            resetButton.Click += new EventHandler(ResetButton_Click);

            buttonPanel.Controls.Add(reloadButton);
            buttonPanel.Controls.Add(applyButton);
            buttonPanel.Controls.Add(resetButton);
            mainLayout.Controls.Add(buttonPanel, 0, 0);

            // Content Tabs
            TabControl tabControl = new TabControl();
            tabControl.Dock = DockStyle.Fill;
            mainLayout.Controls.Add(tabControl, 0, 1);

            // 1. Buttons Tab
            TabPage buttonsTab = new TabPage("Buttons");
            tabControl.TabPages.Add(buttonsTab);
            CreateListTab(buttonsTab, "Key");

            // 2. Macros Tab
            TabPage macrosTab = new TabPage("Macros");
            tabControl.TabPages.Add(macrosTab);
            CreateListTab(macrosTab, "Macro");

            // 3. Settings Tab
            TabPage settingsTab = new TabPage("Settings");
            tabControl.TabPages.Add(settingsTab);
            CreateSettingsTab(settingsTab);

            // 4. Profiles Tab
            TabPage profilesTab = new TabPage("Profiles");
            tabControl.TabPages.Add(profilesTab);
            CreateProfilesTab(profilesTab);

            // Status
            statusLabel = new Label();
            statusLabel.Dock = DockStyle.Fill;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusLabel.Text = "Ready.";
            mainLayout.Controls.Add(statusLabel, 0, 2);

            this.Shown += (s, e) => 
            {
                Application.DoEvents();
                try
                {
                    device = new GpdDevice();
                    device.Open();
                    LoadConfig();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Could not connect to device: " + ex.Message);
                    statusLabel.Text = "Disconnected.";
                    currentConfig = null;
                    device = null;
                    SetConnectedState(false);
                    GuiLogger.LogException("Initial device connection failed", ex);
                }
            };

            SetConnectedState(false);
            GuiLogger.Log("MainForm initialized.");
        }

        private Dictionary<string, ListBox> tabLists = new Dictionary<string, ListBox>();
        private Dictionary<string, ComboBox> tabCombos = new Dictionary<string, ComboBox>();
        private Dictionary<string, Button> tabCaptureButtons = new Dictionary<string, Button>();
        // For settings tab controls
        private Dictionary<string, Control> settingControls = new Dictionary<string, Control>();
        private bool _suppressComboEvents;

        private void SetConnectedState(bool connected)
        {
            applyButton.Enabled = connected;
        }

        private bool EnsureConnectedAndLoaded()
        {
            if (device == null)
            {
                try
                {
                    device = new GpdDevice();
                    device.Open();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Could not connect to device: " + ex.Message);
                    statusLabel.Text = "Disconnected.";
                    SetConnectedState(false);
                    GuiLogger.LogException("EnsureConnectedAndLoaded connection failed", ex);
                    return false;
                }
            }

            if (currentConfig == null)
            {
                LoadConfig();
                if (currentConfig == null) return false;
            }

            return true;
        }

        private bool TryResolveProfilePath(string profileName, out string fullPath, out string error)
        {
            fullPath = null;
            error = null;

            if (string.IsNullOrWhiteSpace(profileName))
            {
                error = "Profile name cannot be empty.";
                return false;
            }

            if (profileName.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 || profileName.Contains("\\") || profileName.Contains("/"))
            {
                error = "Profile name contains invalid characters.";
                return false;
            }

            string profilesDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "profiles");
            string baseDir = System.IO.Path.GetFullPath(profilesDir).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
                + System.IO.Path.DirectorySeparatorChar;
            string candidate = System.IO.Path.GetFullPath(System.IO.Path.Combine(profilesDir, profileName + ".txt"));

            if (!candidate.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
            {
                error = "Profile path escapes the profiles directory.";
                return false;
            }

            fullPath = candidate;
            return true;
        }

        private void PopulateKeyCombo(ComboBox combo)
        {
            combo.Items.Clear();
            foreach (string key in KeyCodes.Map.Keys) combo.Items.Add(key);
            combo.AutoCompleteSource = AutoCompleteSource.ListItems;
            combo.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        }

        private void SetCaptureButtonState(string type, bool enabled)
        {
            Button captureButton;
            if (tabCaptureButtons.TryGetValue(type, out captureButton))
            {
                captureButton.Enabled = enabled;
                captureButton.Visible = enabled;
            }
        }

        private bool TryApplyComboValue(ComboBox combo, bool showMessage)
        {
            if (combo == null) return true;
            ListBox list = combo.Tag as ListBox;
            if (list == null || !(list.SelectedItem is ConfigItem)) return true;

            ConfigItem item = (ConfigItem)list.SelectedItem;
            string val = combo.Text == null ? string.Empty : combo.Text.Trim();
            if (string.IsNullOrWhiteSpace(val)) return true;

            try
            {
                if (item.Def.Type == "Millis")
                {
                    ushort parsedMs;
                    if (!ushort.TryParse(val, out parsedMs))
                    {
                        throw new Exception("Delay must be an integer between 0 and 65535.");
                    }
                }

                item.Config.Set(item.Def.Name, val);
                list.Refresh();
                return true;
            }
            catch (Exception ex)
            {
                string msg = "Invalid value for '" + item.Def.Name + "': " + ex.Message;
                statusLabel.Text = msg;
                GuiLogger.Log(msg);
                if (showMessage) MessageBox.Show(msg);
                return false;
            }
        }

        private bool CommitAllEditorValues(bool showMessageForFirstError)
        {
            bool ok = true;
            bool shown = false;
            foreach (var kvp in tabCombos)
            {
                bool show = showMessageForFirstError && !shown;
                bool thisOk = TryApplyComboValue(kvp.Value, show);
                if (!thisOk)
                {
                    ok = false;
                    shown = true;
                }
            }
            return ok;
        }

        private void CreateListTab(TabPage tab, string filterType)
        {
            SplitContainer contentSplit = new SplitContainer();
            contentSplit.Dock = DockStyle.Fill;
            tab.Controls.Add(contentSplit);

            ListBox list = new ListBox();
            list.Dock = DockStyle.Fill;
            list.Tag = filterType; // Store filter
            list.SelectedIndexChanged += new EventHandler(KeyList_SelectedIndexChanged);
            contentSplit.Panel1.Controls.Add(list);
            tabLists[filterType] = list;

            FlowLayoutPanel editPanel = new FlowLayoutPanel();
            editPanel.Dock = DockStyle.Fill;
            editPanel.FlowDirection = FlowDirection.TopDown;
            Label lbl = new Label();
            lbl.Text = "New Value:";
            lbl.AutoSize = true;
            editPanel.Controls.Add(lbl);
            
            ComboBox combo = new ComboBox();
            combo.Width = 200;
            combo.DropDownStyle = (filterType == "Key" || filterType == "Macro") ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList;
            combo.Tag = list; // Link back to list
            
            if (filterType == "Key" || filterType == "Macro") PopulateKeyCombo(combo);
            // For Macros, we might want delays (Millis) too? 
            // Handled by filter logic later.

            combo.SelectedIndexChanged += new EventHandler(ValueCombo_SelectedIndexChanged);
            combo.LostFocus += new EventHandler(ValueCombo_LostFocus);
            
            editPanel.Controls.Add(combo);

            if (filterType == "Key" || filterType == "Macro")
            {
                Label hint = new Label();
                hint.AutoSize = true;
                hint.Text = "Type key name or hex keycode (e.g. 0xEA).";
                editPanel.Controls.Add(hint);
            }

            if (filterType == "Key" || filterType == "Macro")
            {
                Button capBtn = new Button();
                capBtn.Text = "Capture Key";
                capBtn.AutoSize = true;
                capBtn.Tag = combo; // Link to combo
                capBtn.Click += CaptureKey_Click;
                capBtn.Visible = (filterType == "Key");
                editPanel.Controls.Add(capBtn);
                tabCaptureButtons[filterType] = capBtn;
            }

            contentSplit.Panel2.Controls.Add(editPanel);
            tabCombos[filterType] = combo;
        }

        private void CreateSettingsTab(TabPage tab)
        {
            FlowLayoutPanel panel = new FlowLayoutPanel();
            panel.Dock = DockStyle.Fill;
            panel.FlowDirection = FlowDirection.TopDown;
            panel.AutoScroll = true;
            panel.Padding = new Padding(10);
            tab.Controls.Add(panel);
            
            // We will populate this dynamically in RefreshList or statically here?
            // Statically is better for layout, but we need the FieldDefs.
            // Since FieldDefs are static in Config, we can access them.
            
            foreach (Config.FieldDef def in Config.Fields)
            {
                if (def.Type == "Key" || def.Type == "Millis") continue; // Skip keys/macros here
                // This catches Rumble, LedMode, Colour, Signed (Deadzone)
                
                Panel row = new Panel();
                row.Width = 550;
                row.Height = 40;
                
                Label lbl = new Label();
                lbl.Text = def.Desc + ":";
                lbl.Width = 200;
                lbl.Location = new Point(0, 8);
                row.Controls.Add(lbl);
                
                Control inputCtrl = null;
                
                if (def.Type == "Rumble")
                {
                    ComboBox cb = new ComboBox();
                    cb.DropDownStyle = ComboBoxStyle.DropDownList;
                    cb.Items.Add("Off (0)");
                    cb.Items.Add("Low (1)");
                    cb.Items.Add("High (2)");
                    inputCtrl = cb;
                }
                else if (def.Type == "LedMode")
                {
                    ComboBox cb = new ComboBox();
                    cb.DropDownStyle = ComboBoxStyle.DropDownList;
                    cb.Items.Add("Off (0)");
                    cb.Items.Add("Solid (1)");
                    cb.Items.Add("Breathe (0x11)");
                    cb.Items.Add("Rotate (0x21)");
                    inputCtrl = cb;
                }
                else if (def.Type == "Colour")
                {
                    Button btn = new Button();
                    btn.Text = "Pick Color";
                    btn.Click += (s, e) => {
                        ColorDialog cd = new ColorDialog();
                        if (cd.ShowDialog() == DialogResult.OK)
                        {
                            btn.BackColor = cd.Color;
                            // Store value?
                            currentConfig.Set(def.Name, string.Format("{0:X2}{1:X2}{2:X2}", cd.Color.R, cd.Color.G, cd.Color.B));
                        }
                    };
                    inputCtrl = btn;
                }
                else if (def.Type == "Signed")
                {
                    NumericUpDown nud = new NumericUpDown();
                    nud.Minimum = -128;
                    nud.Maximum = 127;
                    inputCtrl = nud;
                }
                
                if (inputCtrl != null)
                {
                    inputCtrl.Location = new Point(210, 5);
                    inputCtrl.Width = 150;
                    inputCtrl.Tag = def; // Store field def
                    // Add change handler
                    if (inputCtrl is ComboBox) ((ComboBox)inputCtrl).SelectedIndexChanged += Setting_Changed;
                    if (inputCtrl is NumericUpDown) ((NumericUpDown)inputCtrl).ValueChanged += Setting_Changed;
                    
                    row.Controls.Add(inputCtrl);
                    settingControls[def.Name] = inputCtrl;
                }
                
                panel.Controls.Add(row);
            }
        }

        private ListBox profilesList;

        private void CreateProfilesTab(TabPage tab)
        {
            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            tab.Controls.Add(split);

            profilesList = new ListBox();
            profilesList.Dock = DockStyle.Fill;
            split.Panel1.Controls.Add(profilesList);

            FlowLayoutPanel buttonPanel = new FlowLayoutPanel();
            buttonPanel.Dock = DockStyle.Fill;
            buttonPanel.FlowDirection = FlowDirection.TopDown;
            split.Panel2.Controls.Add(buttonPanel);

            Button newBtn = new Button(); newBtn.Text = "New Profile"; newBtn.Width = 150; newBtn.AutoSize = true;
            newBtn.Click += NewProfile_Click;
            buttonPanel.Controls.Add(newBtn);

            Button delBtn = new Button(); delBtn.Text = "Delete Profile"; delBtn.Width = 150; delBtn.AutoSize = true;
            delBtn.Click += DeleteProfile_Click;
            buttonPanel.Controls.Add(delBtn);

            Button editBtn = new Button(); editBtn.Text = "Edit (Load to GUI)"; editBtn.Width = 150; editBtn.AutoSize = true;
            editBtn.Click += EditProfile_Click;
            buttonPanel.Controls.Add(editBtn);

            Button saveBtn = new Button(); saveBtn.Text = "Save GUI to Profile"; saveBtn.Width = 150; saveBtn.AutoSize = true;
            saveBtn.Click += SaveProfileChanges_Click;
            buttonPanel.Controls.Add(saveBtn);

            Button loadBtn = new Button(); loadBtn.Text = "Load (Write to Device)"; loadBtn.Width = 150; loadBtn.AutoSize = true;
            loadBtn.Click += LoadProfile_Click;
            buttonPanel.Controls.Add(loadBtn);

            RefreshProfilesList();
        }

        private void RefreshProfilesList()
        {
            profilesList.Items.Clear();
            string profilesDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "profiles");
            if (System.IO.Directory.Exists(profilesDir))
            {
                string[] files = System.IO.Directory.GetFiles(profilesDir, "*.txt");
                foreach (string file in files)
                {
                    profilesList.Items.Add(System.IO.Path.GetFileNameWithoutExtension(file));
                }
            }
        }

        private void NewProfile_Click(object sender, EventArgs e)
        {
            if (!EnsureConnectedAndLoaded()) return;
            string name = InputBox.Show("New Profile", "Enter profile name:");
            if (!string.IsNullOrWhiteSpace(name))
            {
                string profilesDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "profiles");
                if (!System.IO.Directory.Exists(profilesDir)) System.IO.Directory.CreateDirectory(profilesDir);

                string path;
                string err;
                if (!TryResolveProfilePath(name, out path, out err))
                {
                    MessageBox.Show(err);
                    return;
                }
                try
                {
                    System.IO.File.WriteAllText(path, currentConfig.ToProfileString());
                    RefreshProfilesList();
                    GuiLogger.Log("Profile created: " + name);
                    MessageBox.Show("Profile created.");
                }
                catch (Exception ex) { MessageBox.Show("Error creating profile: " + ex.Message); GuiLogger.LogException("Create profile failed", ex); }
            }
        }

        private void DeleteProfile_Click(object sender, EventArgs e)
        {
            if (profilesList.SelectedItem == null) return;
            string name = profilesList.SelectedItem.ToString();
            if (MessageBox.Show("Delete profile '" + name + "'?", "Confirm", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                string path;
                string err;
                if (!TryResolveProfilePath(name, out path, out err))
                {
                    MessageBox.Show(err);
                    return;
                }
                try
                {
                    System.IO.File.Delete(path);
                    RefreshProfilesList();
                    GuiLogger.Log("Profile deleted: " + name);
                }
                catch (Exception ex) { MessageBox.Show("Error deleting: " + ex.Message); GuiLogger.LogException("Delete profile failed", ex); }
            }
        }

        private void EditProfile_Click(object sender, EventArgs e)
        {
            if (profilesList.SelectedItem == null) return;
            string name = profilesList.SelectedItem.ToString();
            string path;
            string err;
            if (!TryResolveProfilePath(name, out path, out err))
            {
                MessageBox.Show(err);
                return;
            }
            try
            {
                string[] lines = System.IO.File.ReadAllLines(path);
                Config.LoadFromProfile(currentConfig, lines);
                RefreshList(); // Update GUI elements
                statusLabel.Text = "Loaded profile '" + name + "' into GUI (not device).";
                GuiLogger.Log("Profile loaded into GUI state: " + name);
                MessageBox.Show("Profile loaded into GUI. Click 'Apply Changes' to write to device.");
            }
            catch (Exception ex) { MessageBox.Show("Error loading: " + ex.Message); GuiLogger.LogException("Edit/load profile failed", ex); }
        }

        private void LoadProfile_Click(object sender, EventArgs e)
        {
            if (!EnsureConnectedAndLoaded()) return;
            if (profilesList.SelectedItem == null) return;
            string name = profilesList.SelectedItem.ToString();
            if (MessageBox.Show("Write profile '" + name + "' to device firmware?", "Confirm Write", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                string path;
                string err;
                if (!TryResolveProfilePath(name, out path, out err))
                {
                    MessageBox.Show(err);
                    return;
                }
                try
                {
                    // Load to temp config or current? Current is fine as we are applying it.
                    string[] lines = System.IO.File.ReadAllLines(path);
                    Config.LoadFromProfile(currentConfig, lines);
                    RefreshList(); // Sync GUI
                    device.WriteConfig(currentConfig.Raw);
                    statusLabel.Text = "Profile '" + name + "' written to device.";
                    GuiLogger.Log("Profile written to device: " + name);
                    MessageBox.Show("Profile written successfully!");
                }
                catch (Exception ex) { MessageBox.Show("Error writing: " + ex.Message); GuiLogger.LogException("Load profile write failed", ex); }
            }
        }

        private void SaveProfileChanges_Click(object sender, EventArgs e)
        {
            if (currentConfig == null)
            {
                MessageBox.Show("No configuration is loaded in the GUI.");
                return;
            }

            if (profilesList.SelectedItem == null)
            {
                MessageBox.Show("Select a profile first.");
                return;
            }

            string name = profilesList.SelectedItem.ToString();
            string path;
            string err;
            if (!TryResolveProfilePath(name, out path, out err))
            {
                MessageBox.Show(err);
                return;
            }

            try
            {
                System.IO.File.WriteAllText(path, currentConfig.ToProfileString());
                statusLabel.Text = "Profile '" + name + "' saved from GUI state.";
                GuiLogger.Log("Profile saved from GUI state: " + name);
                MessageBox.Show("Profile saved.");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error saving profile: " + ex.Message);
                GuiLogger.LogException("Save GUI profile failed", ex);
            }
        }

        private void Setting_Changed(object sender, EventArgs e)
        {
            Control ctrl = (Control)sender;
            Config.FieldDef def = (Config.FieldDef)ctrl.Tag;
            
            try {
                string val = "";
                if (ctrl is ComboBox)
                {
                    int idx = ((ComboBox)ctrl).SelectedIndex;
                    if (def.Type == "Rumble") val = idx.ToString();
                    if (def.Type == "LedMode") 
                    {
                        if (idx == 0) val = "off";
                        else if (idx == 1) val = "solid";
                        else if (idx == 2) val = "breathe";
                        else if (idx == 3) val = "rotate";
                    }
                }
                else if (ctrl is NumericUpDown)
                {
                    val = ((int)((NumericUpDown)ctrl).Value).ToString();
                }
                
                if (val != "") currentConfig.Set(def.Name, val);
            } catch (Exception ex) { GuiLogger.LogException("Setting_Changed failed for " + def.Name, ex); }
        }

        private void LoadConfig()
        {
            try
            {
                statusLabel.Text = "Reading from device...";
                Application.DoEvents();
                byte[] data = device.ReadConfig();
                currentConfig = new Config(data);
                RefreshList();
                statusLabel.Text = string.Format("Configuration loaded. Firmware: {0}", device.FirmwareVersion);
                SetConnectedState(true);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error reading config: " + ex.Message);
                statusLabel.Text = "Error.";
                currentConfig = null;
                SetConnectedState(false);
                GuiLogger.LogException("LoadConfig failed", ex);
            }
        }

        private void RefreshList()
        {
            foreach (var kvp in tabLists) kvp.Value.Items.Clear();
            
            if (currentConfig != null)
            {
                foreach (Config.FieldDef def in Config.Fields)
                {
                    if (def.Type == "Key")
                    {
                        if (def.Name.Contains("delay")) continue; // Should be Millis type now?
                        // Add to Buttons or Macros?
                        // L4/R4 are macros.
                        string target = (def.Name.StartsWith("l4") || def.Name.StartsWith("r4")) ? "Macro" : "Key";
                        if (tabLists.ContainsKey(target))
                            tabLists[target].Items.Add(new ConfigItem { Def = def, Config = currentConfig });
                    }
                    else if (def.Type == "Millis")
                    {
                        if (tabLists.ContainsKey("Macro"))
                            tabLists["Macro"].Items.Add(new ConfigItem { Def = def, Config = currentConfig });
                    }
                    else
                    {
                        // Settings Tab
                        if (settingControls.ContainsKey(def.Name))
                        {
                            Control ctrl = settingControls[def.Name];
                            string val = currentConfig.GetValue(def);
                            if (ctrl is ComboBox)
                            {
                                ComboBox cb = (ComboBox)ctrl;
                                // Parse value back to index
                                if (def.Type == "Rumble") cb.SelectedIndex = int.Parse(val);
                                if (def.Type == "LedMode")
                                {
                                    if (val == "off") cb.SelectedIndex = 0;
                                    else if (val == "solid") cb.SelectedIndex = 1;
                                    else if (val == "breathe") cb.SelectedIndex = 2;
                                    else if (val == "rotate") cb.SelectedIndex = 3;
                                }
                            }
                            else if (ctrl is NumericUpDown)
                            {
                                NumericUpDown nud = (NumericUpDown)ctrl;
                                int iVal;
                                if (int.TryParse(val, out iVal)) nud.Value = iVal;
                            }
                            else if (ctrl is Button && def.Type == "Colour")
                            {
                                Button btn = (Button)ctrl;
                                // val is RRGGBB hex?
                                // GetValue returns hex string
                                try {
                                    int r = int.Parse(val.Substring(0,2), System.Globalization.NumberStyles.HexNumber);
                                    int g = int.Parse(val.Substring(2,2), System.Globalization.NumberStyles.HexNumber);
                                    int b = int.Parse(val.Substring(4,2), System.Globalization.NumberStyles.HexNumber);
                                    btn.BackColor = Color.FromArgb(r,g,b);
                                } catch {}
                            }
                        }
                    }
                }
            }
        }

        private void KeyList_SelectedIndexChanged(object sender, EventArgs e)
        {
            ListBox list = (ListBox)sender;
            string type = (string)list.Tag;
            ComboBox combo = tabCombos[type];
            
            if (list.SelectedItem is ConfigItem)
            {
                ConfigItem item = (ConfigItem)list.SelectedItem;
                string currentVal = item.Config.GetValue(item.Def);

                if (type == "Macro" && item.Def.Type == "Millis")
                {
                    SetCaptureButtonState(type, false);
                    _suppressComboEvents = true;
                    try
                    {
                        combo.AutoCompleteMode = AutoCompleteMode.None;
                        combo.AutoCompleteSource = AutoCompleteSource.None;
                        combo.DropDownStyle = ComboBoxStyle.DropDown;
                        combo.Text = currentVal;
                    }
                    finally
                    {
                        _suppressComboEvents = false;
                    }
                    return;
                }

                SetCaptureButtonState(type, true);
                _suppressComboEvents = true;
                try
                {
                    if (combo.Items.Count == 0) PopulateKeyCombo(combo);
                    combo.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                    combo.AutoCompleteSource = AutoCompleteSource.ListItems;
                    combo.DropDownStyle = ComboBoxStyle.DropDown;
                    int index = combo.FindStringExact(currentVal);
                    if (index != -1)
                    {
                        combo.SelectedIndex = index;
                    }
                    else
                    {
                        combo.Text = currentVal;
                    }
                }
                finally
                {
                    _suppressComboEvents = false;
                }
            }
            else if (type == "Macro")
            {
                SetCaptureButtonState(type, false);
            }
        }

        private void ValueCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_suppressComboEvents) return;
            TryApplyComboValue((ComboBox)sender, false);
        }
        
        private void ValueCombo_LostFocus(object sender, EventArgs e)
        {
            if (_suppressComboEvents) return;
            TryApplyComboValue((ComboBox)sender, false);
        }

        private void CaptureKey_Click(object sender, EventArgs e)
        {
            Button btn = (Button)sender;
            ComboBox combo = (ComboBox)btn.Tag;
            ListBox list = combo.Tag as ListBox;
            if (list != null && list.SelectedItem is ConfigItem)
            {
                ConfigItem selected = (ConfigItem)list.SelectedItem;
                if (selected.Def.Type == "Millis")
                {
                    MessageBox.Show("Capture Key is only available for key mapping fields.");
                    return;
                }
            }
            
            using (KeyCaptureForm form = new KeyCaptureForm())
            {
                if (form.ShowDialog() == DialogResult.OK)
                {
                    string key = form.CapturedKeyName;
                    int idx = combo.FindStringExact(key);
                    if (idx != -1)
                    {
                        combo.SelectedIndex = idx;
                    }
                    else
                    {
                        combo.DropDownStyle = ComboBoxStyle.DropDown;
                        combo.Text = key;
                        TryApplyComboValue(combo, false);
                    }
                }
            }
        }

        private void ApplyButton_Click(object sender, EventArgs e)
        {
            if (!EnsureConnectedAndLoaded()) return;
            if (!CommitAllEditorValues(true))
            {
                GuiLogger.Log("Apply aborted due to invalid editor value.");
                return;
            }

            try
            {
                statusLabel.Text = "Writing to device...";
                Application.DoEvents();
                device.WriteConfig(currentConfig.Raw);
                statusLabel.Text = "Saved successfully.";
                GuiLogger.Log("Configuration written to device.");
                MessageBox.Show("Configuration saved to device!");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error writing: " + ex.Message);
                statusLabel.Text = "Write failed.";
                GuiLogger.LogException("Apply write failed", ex);
            }
        }

        private void ResetButton_Click(object sender, EventArgs e)
        {
            if (!EnsureConnectedAndLoaded()) return;
            if (MessageBox.Show("Are you sure you want to reset all mappings to default?", "Confirm Reset", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                string mappingPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "default_mappings.txt");
                if (!System.IO.File.Exists(mappingPath))
                {
                    MessageBox.Show("default_mappings.txt was not found.");
                    statusLabel.Text = "Reset failed.";
                    return;
                }

                try
                {
                    string[] lines = System.IO.File.ReadAllLines(mappingPath);
                    Config.LoadFromProfile(currentConfig, lines);
                    device.WriteConfig(currentConfig.Raw);
                    RefreshList();
                    statusLabel.Text = "Defaults restored and written to device.";
                    GuiLogger.Log("Defaults restored and written.");
                    MessageBox.Show("Defaults restored successfully.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Reset failed: " + ex.Message);
                    statusLabel.Text = "Reset failed.";
                    GuiLogger.LogException("Reset failed", ex);
                }
            }
        }

        private void ReloadButton_Click(object sender, EventArgs e)
        {
            if (device == null)
            {
                try
                {
                    device = new GpdDevice();
                    device.Open();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Could not connect to device: " + ex.Message);
                    statusLabel.Text = "Disconnected.";
                    SetConnectedState(false);
                    GuiLogger.LogException("Reload connection failed", ex);
                    return;
                }
            }
            LoadConfig();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (device != null) device.Dispose();
            base.OnFormClosed(e);
        }

        // Helper class for ListBox
        private class ConfigItem
        {
            public Config.FieldDef Def;
            public Config Config;
            
            public override string ToString()
            {
                return string.Format("{0} = {1} ({2})", Def.Name, Config.GetValue(Def), Def.Desc);
            }
        }
    }

    public class KeyCaptureForm : Form
    {
        public string CapturedKeyName { get; private set; }

        public KeyCaptureForm()
        {
            this.Text = "Press any key...";
            this.Size = new Size(300, 150);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            
            Label lbl = new Label();
            lbl.Text = "Press a key on your keyboard/controller to map it.\nPress Escape to cancel.";
            lbl.TextAlign = ContentAlignment.MiddleCenter;
            lbl.Dock = DockStyle.Fill;
            this.Controls.Add(lbl);
            
            this.KeyPreview = true;
            this.KeyDown += KeyCaptureForm_KeyDown;
        }

        private void KeyCaptureForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                this.DialogResult = DialogResult.Cancel;
                this.Close();
                return;
            }

            // Map WinForms Keys to our KeyCodes name
            string name = KeyMapper.Map(e.KeyCode);
            if (name == null)
            {
                // Try direct string match
                string s = e.KeyCode.ToString().ToUpper();
                if (KeyCodes.Map.ContainsKey(s)) name = s;
            }

            if (name != null)
            {
                CapturedKeyName = name;
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
        }
    }

    public static class KeyMapper
    {
        public static string Map(Keys k)
        {
            // Manual mapping for divergent names
            switch (k)
            {
                case Keys.Return: return "ENTER";
                case Keys.Back: return "BACKSPACE";
                case Keys.Capital: return "CAPSLOCK";
                case Keys.Oemcomma: return "COMMA";
                case Keys.OemPeriod: return "DOT";
                case Keys.OemQuestion: return "SLASH"; // ? is /
                case Keys.OemSemicolon: return "SEMICOLON";
                case Keys.OemQuotes: return "APOSTROPHE";
                case Keys.OemOpenBrackets: return "LEFTBRACE";
                case Keys.OemCloseBrackets: return "RIGHTBRACE";
                case Keys.OemPipe: return "BACKSLASH";
                case Keys.Oemtilde: return "GRAVE"; // ~ is `
                case Keys.OemMinus: return "MINUS";
                case Keys.Oemplus: return "EQUAL"; // + is =
                case Keys.D0: return "0";
                case Keys.D1: return "1";
                case Keys.D2: return "2";
                case Keys.D3: return "3";
                case Keys.D4: return "4";
                case Keys.D5: return "5";
                case Keys.D6: return "6";
                case Keys.D7: return "7";
                case Keys.D8: return "8";
                case Keys.D9: return "9";
                case Keys.LWin: return "LWIN";
                case Keys.RWin: return "RWIN";
                case Keys.Apps: return "APPLICATIONS";
                case Keys.PrintScreen: return "SYSRQ";
                case Keys.VolumeUp: return "VOLUP";
                case Keys.VolumeDown: return "VOLDN";
                case Keys.VolumeMute: return "MUTE";
                case Keys.Space: return "SPACE";
                case Keys.Delete: return "DELETE";
                case Keys.Insert: return "INSERT";
                case Keys.Home: return "HOME";
                case Keys.End: return "END";
                case Keys.PageUp: return "PAGEUP";
                case Keys.PageDown: return "PAGEDOWN";
                case Keys.Up: return "UP";
                case Keys.Down: return "DOWN";
                case Keys.Left: return "LEFT";
                case Keys.Right: return "RIGHT";
                case Keys.Tab: return "TAB";
                // Add more as needed
            }
            return null;
        }
    }

    public static class GuiLogger
    {
        private static readonly object Sync = new object();
        private static readonly string LogPath;

        static GuiLogger()
        {
            try
            {
                string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                Directory.CreateDirectory(logDir);
                LogPath = Path.Combine(logDir, "gpdgui-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
            }
            catch
            {
                LogPath = null;
            }
        }

        public static void Log(string message)
        {
            if (string.IsNullOrEmpty(LogPath)) return;
            try
            {
                lock (Sync)
                {
                    File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message + Environment.NewLine);
                }
            }
            catch
            {
            }
        }

        public static void LogException(string context, Exception ex)
        {
            if (ex == null) Log(context);
            else Log(context + ": " + ex.ToString());
        }
    }

    public static class InputBox
    {
        public static string Show(string title, string promptText)
        {
            Form form = new Form();
            Label label = new Label();
            TextBox textBox = new TextBox();
            Button buttonOk = new Button();
            Button buttonCancel = new Button();

            form.Text = title;
            label.Text = promptText;
            textBox.Text = "";

            buttonOk.Text = "OK";
            buttonCancel.Text = "Cancel";
            buttonOk.DialogResult = DialogResult.OK;
            buttonCancel.DialogResult = DialogResult.Cancel;

            label.SetBounds(9, 20, 372, 13);
            textBox.SetBounds(12, 36, 372, 20);
            buttonOk.SetBounds(228, 72, 75, 23);
            buttonCancel.SetBounds(309, 72, 75, 23);

            label.AutoSize = true;
            textBox.Anchor = textBox.Anchor | AnchorStyles.Right;
            buttonOk.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            buttonCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;

            form.ClientSize = new Size(396, 107);
            form.Controls.AddRange(new Control[] { label, textBox, buttonOk, buttonCancel });
            form.ClientSize = new Size(Math.Max(300, label.Right + 10), form.ClientSize.Height);
            form.FormBorderStyle = FormBorderStyle.FixedDialog;
            form.StartPosition = FormStartPosition.CenterScreen;
            form.MinimizeBox = false;
            form.MaximizeBox = false;
            form.AcceptButton = buttonOk;
            form.CancelButton = buttonCancel;

            DialogResult dialogResult = form.ShowDialog();
            return dialogResult == DialogResult.OK ? textBox.Text : "";
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                GuiLogger.LogException("Critical UI exception", ex);
                MessageBox.Show(ex.ToString(), "Critical Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
