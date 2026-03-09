using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace Assets_Editor
{
    public partial class MigrationMenuWindow : Window
    {
        public sealed class MigrationOption
        {
            public string Title { get; set; } = "";
            public string Description { get; set; } = "";
            public string Kind { get; set; } = "";
        }

        public MigrationMenuWindow()
        {
            InitializeComponent();
            MigrationList.ItemsSource = new List<MigrationOption>
            {
                new MigrationOption
                {
                    Title = "Items",
                    Description = "Old items.xml (+ optional items.otb) → Canary data/items/items.xml",
                    Kind = "items"
                },
                new MigrationOption
                {
                    Title = "Outfits",
                    Description = "Old data/XML/outfits.xml → Canary data/XML/outfits.xml",
                    Kind = "outfits"
                }
            };
        }

        private void RunSelected_Click(object sender, RoutedEventArgs e)
        {
            if (MigrationList.SelectedItem is not MigrationOption opt)
            {
                MessageBox.Show("Select a migration from the list.", "Migrations", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (opt.Kind == "items")
            {
                var win = new MigrateItemsWindow { Owner = this };
                win.ShowDialog();
            }
            else if (opt.Kind == "outfits")
            {
                var win = new MigrateOutfitsWindow { Owner = this };
                win.ShowDialog();
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
