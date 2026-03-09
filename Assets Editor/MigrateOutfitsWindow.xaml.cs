using System;
using System.Windows;
using Microsoft.Win32;

namespace Assets_Editor
{
    public partial class MigrateOutfitsWindow : Window
    {
        public MigrateOutfitsWindow()
        {
            InitializeComponent();
        }

        private void BrowseOldOutfits(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select old server outfits.xml",
                Filter = "XML files (*.xml)|*.xml|All files (*.*)|*.*",
                FileName = "outfits.xml"
            };
            if (dlg.ShowDialog() == true)
                OldOutfitsXmlPath.Text = dlg.FileName;
        }

        private void BrowseOutput(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title = "Select output Canary outfits.xml",
                Filter = "XML files (*.xml)|*.xml|All files (*.*)|*.*",
                FileName = "outfits.xml"
            };
            if (dlg.ShowDialog() == true)
                OutputOutfitsXmlPath.Text = dlg.FileName;
        }

        private void RunMigration_Click(object sender, RoutedEventArgs e)
        {
            string oldPath = OldOutfitsXmlPath.Text?.Trim() ?? "";
            string outputPath = OutputOutfitsXmlPath.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(oldPath))
            {
                MessageBox.Show("Please select old server outfits.xml.", "Migration", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrEmpty(outputPath))
            {
                MessageBox.Show("Please select output outfits.xml path.", "Migration", MessageBoxButton.OK, MessageBoxImage.Warning);
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

                int written = OutfitsMigration.Migrate(oldPath, outputPath, progress);

                LogText.AppendText($"\nDone. Wrote {written} outfits to {outputPath}\n");
                MessageBox.Show($"Migration completed. Wrote {written} outfits to:\n{outputPath}", "Migration", MessageBoxButton.OK, MessageBoxImage.Information);
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
