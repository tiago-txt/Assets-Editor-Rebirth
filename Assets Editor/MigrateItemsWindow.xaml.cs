using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Assets_Editor
{
    public partial class MigrateItemsWindow : Window
    {
        public MigrateItemsWindow()
        {
            InitializeComponent();
        }

        private void BrowseOldItemsXml(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select old server items.xml",
                Filter = "XML files (*.xml)|*.xml|All files (*.*)|*.*",
                FileName = "items.xml"
            };
            if (dlg.ShowDialog() == true)
                OldItemsXmlPath.Text = dlg.FileName;
        }

        private void BrowseOldOtb(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select old server items.otb (optional)",
                Filter = "OTB files (*.otb)|*.otb|All files (*.*)|*.*",
                FileName = "items.otb"
            };
            if (dlg.ShowDialog() == true)
                OldOtbPath.Text = dlg.FileName;
        }

        private void BrowseOutput(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title = "Select output Canary items.xml",
                Filter = "XML files (*.xml)|*.xml|All files (*.*)|*.*",
                FileName = "items.xml"
            };
            if (dlg.ShowDialog() == true)
                OutputItemsXmlPath.Text = dlg.FileName;
        }

        private void BrowseTargetAppearances(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select Canary appearance.dat (optional validation)",
                Filter = "Appearance files (*.dat;*.aec)|*.dat;*.aec|All files (*.*)|*.*",
                FileName = "appearance.dat"
            };
            if (dlg.ShowDialog() == true)
                TargetAppearancesPath.Text = dlg.FileName;
        }

        private void RunMigration_Click(object sender, RoutedEventArgs e)
        {
            string oldXml = OldItemsXmlPath.Text?.Trim() ?? "";
            string output = OutputItemsXmlPath.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(oldXml))
            {
                MessageBox.Show("Please select old server items.xml.", "Migration", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrEmpty(output))
            {
                MessageBox.Show("Please select output items.xml path.", "Migration", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            LogText.Clear();
            ProgressBar.Value = 0;
            StatusText.Text = "";
            RunMigration.IsEnabled = false;

            try
            {
                var progress = new Progress<(int current, int total, string message)>(p =>
                {
                    if (p.total > 0)
                        ProgressBar.Value = Math.Min(100, (p.current * 100) / Math.Max(1, p.total));
                    StatusText.Text = p.message;
                    LogText.AppendText(p.message + "\n");
                    LogText.ScrollToEnd();
                });

                int written = ItemsMigration.Migrate(
                    oldXml,
                    string.IsNullOrWhiteSpace(OldOtbPath.Text) ? null : OldOtbPath.Text.Trim(),
                    string.IsNullOrWhiteSpace(TargetAppearancesPath.Text) ? null : TargetAppearancesPath.Text.Trim(),
                    output,
                    progress);

                LogText.AppendText($"\nDone. Wrote {written} items to {output}\n");
                MessageBox.Show($"Migration completed. Wrote {written} items to:\n{output}", "Migration", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogText.AppendText($"\nError: {ex.Message}\n");
                MessageBox.Show($"Migration failed:\n{ex.Message}", "Migration", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                RunMigration.IsEnabled = true;
                ProgressBar.Value = 100;
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
