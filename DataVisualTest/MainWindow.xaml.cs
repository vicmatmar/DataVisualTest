using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using System.Windows.Controls.DataVisualization.Charting;
using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace DataVisualTest
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        ObservableCollection<KeyValuePair<string, int>> valueList;
        ObservableCollection<KeyValuePair<DateTime, double>> power;

        DispatcherTimer timer;
        int value = 0;
        Random random = new Random(DateTime.Now.Millisecond);

        public MainWindow()
        {
            InitializeComponent();
            showColumnChart();

            timer = new DispatcherTimer();
            timer.Interval = new TimeSpan(0, 0, 1);
            timer.Tick += Timer_Tick;
            timer.Start();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            changeData();
        }

        void changeData()
        {
            valueList[0] = new KeyValuePair<string, int>("Developer", value++);

            power.Add(new KeyValuePair<DateTime, double>(DateTime.Now, random.NextDouble()));
            
        }
        private void showColumnChart()
        {
            valueList = new ObservableCollection<KeyValuePair<string, int>>();
            valueList.Add(new KeyValuePair<string, int>("Developer", 60));
            valueList.Add(new KeyValuePair<string, int>("Misc", 20));
            valueList.Add(new KeyValuePair<string, int>("Tester", 50));
            valueList.Add(new KeyValuePair<string, int>("QA", 30));
            valueList.Add(new KeyValuePair<string, int>("Project Manager", 40));

            power = new ObservableCollection<KeyValuePair<DateTime, double>>();

            //Setting data for column chart
            columnChart.DataContext = valueList;

            lineChart.DataContext = power;

        }
    }
}
