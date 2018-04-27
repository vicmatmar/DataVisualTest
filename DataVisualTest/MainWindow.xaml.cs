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

using CINALib;
using FTD2XX_NET;
using System.IO;
using System.Threading;
using System.ComponentModel;
using Microsoft.Win32;
using System.Data;
using System.Threading.Tasks;

namespace DataVisualTest
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        ObservableCollection<KeyValuePair<double, double>> voltage, current, power;

        DispatcherTimer timer;
        Random random = new Random(DateTime.Now.Millisecond);

        Cina219 cina = new Cina219();

        StreamWriter _sw;

        int _duration_sec = 60;
        public int Duration_sec { get => _duration_sec; set => _duration_sec = value; }

        Object dataLock = new Object();


        public MainWindow()
        {
            InitializeComponent();

            lblStatus.Text = DateTime.Now.ToLongTimeString();

            try
            {
                cina.UnitCurrent = Cina219.CurrentUnit.mA;
                cina.Init();
                cina.SetACBusPin(0, false);


                timer = new DispatcherTimer();
                timer.Tick += Timer_Tick;

            }
            catch (Exception ex)
            {
                lblStatus.Text = $"{DateTime.Now.ToShortTimeString()} {ex.Message}";
            }

        }

        protected override void OnClosing(CancelEventArgs e)
        {
            try
            {
                // Try to turn relay off
                cina.Init();
                cina.SetACBusPin(0, false);
            }
            catch (Exception ex)
            {
                // and just in case that failed
                cina.Dispose();
                Cina219 cina2 = new Cina219();
                cina2.Init();
            }

            base.OnClosing(e);
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            getData();

            TimeSpan ts = DateTime.Now - (DateTime)timer.Tag;
            if (ts.TotalSeconds > Duration_sec)
            {
                stop();
                btnStart.Content = "Start";
            }
        }

        void stop()
        {
            timer.Stop();
            cina.SetACBusPin(0, false);
            _sw.Close();
        }

        void getData()
        {
            lock (dataLock)
            {
                DateTime dateTime = DateTime.Now;
                DateTime startTime = (DateTime)timer.Tag;
                TimeSpan ts = DateTime.Now - startTime;

                bool ovf = false;
                float v = cina.GetVoltage(ref ovf);
                float i = cina.GetCurrent();
                float p = v * i;


                voltage.Add(new KeyValuePair<double, double>(ts.TotalMilliseconds, v));
                if (voltage.Count > 1)
                {
                    if (i > 0)
                        current.Add(new KeyValuePair<double, double>(ts.TotalMilliseconds, i));
                    if (p > 0)
                        power.Add(new KeyValuePair<double, double>(ts.TotalMilliseconds, p));
                }

                double rate_fromStart = 0;
                if (power.Count > 0)
                {
                    double deltaPower_fromStart = p - power[0].Value;
                    rate_fromStart = deltaPower_fromStart / ts.TotalHours; // mw/h
                }

                string guimsg = $"{dateTime.ToShortTimeString()}";
                guimsg += $" {v.ToString("F2")}V";
                guimsg += $" {i.ToString("F2")}mA";
                guimsg += $" {p.ToString("F2")}mW";
                guimsg += $" {rate_fromStart.ToString("F2")}mW/h";
                guimsg += $" {ts.TotalSeconds.ToString("F2")}s";
                lblMsg.Content = guimsg;


                writeLineDataToFile(_sw, $"{dateTime},{p},{v},{i},{ts.Ticks}");
            }
        }

        void writeLineDataToFile(StreamWriter sw, string data)
        {
            int trycount = 0;
            while (true)
            {
                try
                {
                    sw.WriteLine(data);
                    break;
                }
                catch (Exception ex)
                {
                    lblStatus.Text = $"Try# {trycount}, {DateTime.Now.ToShortTimeString()} {ex.Message}";
                    if (trycount++ > 10)
                        break;
                }
            }
        }

        private void Import_MenuItem_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog();
            dlg.Filter = "*.csv|*.csv|All Files|*.*";

            bool b = dlg.ShowDialog() ?? false;
            if (!b)
                return;

            string filename = dlg.FileName;

            importData(filename);

        }

        async void importData(string filename)
        {
            List<KeyValuePair<double, double>> volts_list = new List<KeyValuePair<double, double>>();
            List<KeyValuePair<double, double>> current_list = new List<KeyValuePair<double, double>>();
            List<KeyValuePair<double, double>> power_list = new List<KeyValuePair<double, double>>();

            await Task.Run(() =>
            {
                DataTable tdata = CsvToDataTable(filename);

                foreach (DataRow r in tdata.Rows)
                {
                    TimeSpan ts = new TimeSpan((long)r["Duration"]);

                    volts_list.Add(
                         new KeyValuePair<double, double>(
                             ts.TotalMilliseconds, (double)r["Voltage(V)"]));

                    current_list.Add(
                         new KeyValuePair<double, double>(
                             ts.TotalMilliseconds, (double)r["Current(mA)"]));

                    power_list.Add(
                         new KeyValuePair<double, double>(
                             ts.TotalMilliseconds, (double)r["Power(mW)"]));
                }
            });

            if (current_list.Count > 0)
                current_list.RemoveAt(0);
            if (power_list.Count > 0)
                power_list.RemoveAt(0);

            string title = System.IO.Path.GetFileNameWithoutExtension(filename);
            LineSeries series = new LineSeries();
            series.Title = title;
            series.ItemsSource = volts_list;
            series.DependentValuePath = "Value";
            series.IndependentValuePath = "Key";
            series.IndependentAxis = ((LineSeries)lineVolts.Series[0]).IndependentAxis;
            series.DependentRangeAxis = ((LineSeries)lineVolts.Series[0]).DependentRangeAxis;
            lineVolts.Series.Add(series);

            series = new LineSeries();
            series.Title = title;
            series.ItemsSource = current_list;
            series.DependentValuePath = "Value";
            series.IndependentValuePath = "Key";
            series.IndependentAxis = ((LineSeries)lineCurrent.Series[0]).IndependentAxis;
            series.DependentRangeAxis = ((LineSeries)lineCurrent.Series[0]).DependentRangeAxis;
            lineCurrent.Series.Add(series);

            series = new LineSeries();
            series.Title = title;
            series.ItemsSource = power_list;
            series.DependentValuePath = "Value";
            series.IndependentValuePath = "Key";
            series.IndependentAxis = ((LineSeries)linePower.Series[0]).IndependentAxis;
            series.DependentRangeAxis = ((LineSeries)linePower.Series[0]).DependentRangeAxis;
            linePower.Series.Add(series);

        }

        DataTable CsvToDataTable(string filename)
        {
            // Read the file
            string data = "";
            using (StreamReader sr = new StreamReader(filename))
            {
                data = sr.ReadToEnd();
            }

            // Split to lines
            var lines = data.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0)
                throw new Exception("No lines");

            // Create table columns
            string[] col_names = lines[0].Split(new[] { ',' });
            DataTable table = new DataTable(filename);
            foreach (var cname in col_names)
            {
                DataColumn c = new DataColumn(cname, typeof(double));
                if (cname == "TimeStamp")
                    c = new DataColumn(cname, typeof(DateTime));
                else if (cname == "Duration")
                    c = new DataColumn(cname, typeof(long));
                table.Columns.Add(c);
            }

            /*
            int iDate = Array.FindIndex<string>(col_names, n => n == "TimeStamp");
            int iPower = Array.FindIndex<string>(col_names, n => n.StartsWith("Power"));
            int iVolatge = Array.FindIndex<string>(col_names, n => n.StartsWith("Voltage"));
            int iCurrent = Array.FindIndex<string>(col_names, n => n.StartsWith("Current"));
            */

            // get the rows
            for (int i = 1; i < lines.Length; i++)
            {
                var fields = lines[i].Split(new[] { ',' });
                table.Rows.Add(fields);
            }

            return table;
        }

        private void Clear_Voltage(object sender, RoutedEventArgs e)
        {

            int count = lineVolts.Series.Count;
            for (int i = 1; i < count; i++)
            {
                lineVolts.Series.RemoveAt(1);
            }

        }


        private void Button_StartClick(object sender, RoutedEventArgs e)
        {
            if (btnStart.Content.ToString() == "Start")
            {
                btnStart.Content = "Stop";
                Duration_sec = Convert.ToInt32(txtDuration.Text);

                voltage = new ObservableCollection<KeyValuePair<double, double>>();
                current = new ObservableCollection<KeyValuePair<double, double>>();
                power = new ObservableCollection<KeyValuePair<double, double>>();

                lineVolts.DataContext = voltage;
                lineCurrent.DataContext = current;
                linePower.DataContext = power;

                string filename = $"BatteryProfile{DateTime.Now.ToString("MMdd_HHmm")}.csv";
                _sw = File.CreateText(filename);
                _sw.AutoFlush = true;
                _sw.WriteLine($"TimeStamp,Power(mW),Voltage(V),Current(mA),Duration");

                timer.Interval = new TimeSpan(0, 0, 0, 0, Int32.Parse(txtInterval.Text));
                timer.Tag = DateTime.Now;
                timer.Start();

                getData();

                cina.SetACBusPin(0, true);

            }
            else
            {
                stop();
                btnStart.Content = "Start";
            }
        }

    }
}
