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
using System.Runtime.CompilerServices;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DataVisualTest
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window , INotifyPropertyChanged
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

            ClearImportedMenuItem.DataContext = this;

        }

        protected override void OnClosing(CancelEventArgs e)
        {
            try
            {
                // Try to turn relay off
                cina.Init();
                cina.SetACBusPin(0, false);
            }
            catch
            {
                // and just in case that failed
                cina.Dispose();
                Cina219 cina2 = new Cina219();
                cina2.Init();
            }

            base.OnClosing(e);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string property)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        }

        public bool HasImportedData
        {
            get {
                return (linePower.Series.Count > 1 || lineVolts.Series.Count > 1) || lineCurrent.Series.Count > 1;
            }
            set {
                OnPropertyChanged("HasImportedData");
            }
        }

        async void Timer_Tick(object sender, EventArgs e)
        {
            await getDataAsync();

            TimeSpan ts = DateTime.Now - (DateTime)timer.Tag;
            if (ts.TotalSeconds > Duration_sec)
            {
                //string msg = t.Result;  // This forces the task to wait...

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

        async Task getDataAsync()
        {
            DateTime dateTime = DateTime.Now;
            DateTime startTime = (DateTime)timer.Tag;
            TimeSpan ts = DateTime.Now - startTime;

            //bool ovf = false;
            float v = await cina.GetVoltageAsync();
            float i = await cina.GetCurrentAsync();
            float p = v * i;

            voltage.Add(new KeyValuePair<double, double>(ts.TotalMilliseconds, v));
            if (voltage.Count > 1)
            {
//                if (i > 0)
                    current.Add(new KeyValuePair<double, double>(ts.TotalMilliseconds, i));
//                if (p > 0)
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

            await writeLineDataToFileAsync(_sw, $"{dateTime},{p},{v},{i},{ts.Ticks}");

        }

        async Task writeLineDataToFileAsync(StreamWriter sw, string data)
        {
            int trycount = 0;
            while (true)
            {
                try
                {
                    await sw.WriteLineAsync(data);
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
        async void Import_MenuItem_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog();
            dlg.Filter = "*.csv|*.csv|All Files|*.*";

            bool b = dlg.ShowDialog() ?? false;
            if (!b)
                return;

            await ImportDataAndUpdateCharts(dlg.FileName);

            HasImportedData = true;
        }

        private async Task ImportDataAndUpdateCharts(string filename)
        {
            var map = await ImportData(filename);

            string title = System.IO.Path.GetFileNameWithoutExtension(filename);

            LineSeries series = new LineSeries();
            series.Title = title;
            series.ItemsSource = map["Voltage"];
            series.DependentValuePath = "Value";
            series.IndependentValuePath = "Key";
            series.IndependentAxis = ((LineSeries)lineVolts.Series[0]).IndependentAxis;
            series.DependentRangeAxis = ((LineSeries)lineVolts.Series[0]).DependentRangeAxis;
            lineVolts.Series.Add(series);

            series = new LineSeries();
            series.Title = title;
            series.ItemsSource = map["Current"];
            series.DependentValuePath = "Value";
            series.IndependentValuePath = "Key";
            series.IndependentAxis = ((LineSeries)lineCurrent.Series[0]).IndependentAxis;
            series.DependentRangeAxis = ((LineSeries)lineCurrent.Series[0]).DependentRangeAxis;
            lineCurrent.Series.Add(series);

            series = new LineSeries();
            series.Title = title;
            series.ItemsSource = map["Power"];
            series.DependentValuePath = "Value";
            series.IndependentValuePath = "Key";
            series.IndependentAxis = ((LineSeries)linePower.Series[0]).IndependentAxis;
            series.DependentRangeAxis = ((LineSeries)linePower.Series[0]).DependentRangeAxis;
            linePower.Series.Add(series);

        }

        async Task<Dictionary<string, List<KeyValuePair<double, double>>>> ImportData(string filename)
        {
            List<KeyValuePair<double, double>> volts_list = new List<KeyValuePair<double, double>>();
            List<KeyValuePair<double, double>> current_list = new List<KeyValuePair<double, double>>();
            List<KeyValuePair<double, double>> power_list = new List<KeyValuePair<double, double>>();

            DataTable tdata = await CsvToDataTableAsync(filename);

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

            if (current_list.Count > 0)
                current_list.RemoveAt(0);
            if (power_list.Count > 0)
                power_list.RemoveAt(0);

            Dictionary<string, List<KeyValuePair<double, double>>> map = new Dictionary<string, List<KeyValuePair<double, double>>>();
            map.Add("Voltage", volts_list);
            map.Add("Current", current_list);
            map.Add("Power", power_list);

            return map;

        }

        async Task<DataTable> CsvToDataTableAsync(string filename)
        {
            // Read the file
            string data = "";
            using (StreamReader sr = File.OpenText(filename))
            {
                data = await sr.ReadToEndAsync();
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

        private void Clear_Imported_Charts_MenuItem_Click(object sender, RoutedEventArgs e)
        {
            int count = lineVolts.Series.Count;
            for (int i = 1; i < count; i++)
            {
                lineVolts.Series.RemoveAt(1);
            }
            count = lineCurrent.Series.Count;
            for (int i = 1; i < count; i++)
            {
                lineCurrent.Series.RemoveAt(1);
            }
            count = linePower.Series.Count;
            for (int i = 1; i < count; i++)
            {
                linePower.Series.RemoveAt(1);
            }
            HasImportedData = false;
        }

        private void Close_Window_MenuItem_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Clear_Voltage(object sender, RoutedEventArgs e)
        {

            int count = lineVolts.Series.Count;
            for (int i = 1; i < count; i++)
            {
                lineVolts.Series.RemoveAt(1);
            }

        }

        string getValidFileName(string fileName)
        {
            return System.IO.Path.GetInvalidFileNameChars().Aggregate(fileName, (current, c) => current.Replace(c.ToString(), string.Empty));
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

                //string filename = $"BatteryProfile{DateTime.Now.ToString("MMdd_HHmm")}.csv";
                string filename = $"{getValidFileName(txtFileName.Text)}.csv";

                _sw = File.CreateText(filename);
                _sw.AutoFlush = true;
                _sw.WriteLine($"TimeStamp,Power(mW),Voltage(V),Current(mA),Duration");

                timer.Interval = new TimeSpan(0, 0, 0, 0, Int32.Parse(txtInterval.Text));

                cina.SetACBusPin(0, true);
                timer.Tag = DateTime.Now;
                timer.Start();

                Task t = getDataAsync();


            }
            else
            {
                stop();
                btnStart.Content = "Start";
            }
        }
    }

    public class FileNameRule : ValidationRule
    {
        public override ValidationResult Validate(object value, CultureInfo cultureInfo)
        {
            string fileName = (string)value;

            string strTheseAreInvalidFileNameChars = new string(System.IO.Path.GetInvalidFileNameChars());
            Regex regInvalidFileName = new Regex("[" + Regex.Escape(strTheseAreInvalidFileNameChars) + "]");

            bool isValidName = !regInvalidFileName.IsMatch(fileName);
            if (isValidName)
                return ValidationResult.ValidResult;
            else
                return new ValidationResult(false, "Invalid character");
        }
    }
}
