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
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        ObservableCollection<KeyValuePair<double, double>> voltage_colection, current_colection, power_collaction;

        DispatcherTimer _timer;
        Random random = new Random(DateTime.Now.Millisecond);

        Cina219 _cina = new Cina219();

        StreamWriter _sw;

        double _load_on_duration_sec = 15;
        double _rest_duration_sec = 2;
        int _repeat_count = 0;
        int _interval = 100;


        public MainWindow()
        {
            InitializeComponent();

            lblStatus.Text = DateTime.Now.ToLongTimeString();

            try
            {
                _cina.UnitCurrent = Cina219.CurrentUnit.mA;
                _cina.Init();
                _cina.SetACBusPin(0, false);

                _timer = new DispatcherTimer();

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
                _cina.Init();
                _cina.SetACBusPin(0, false);
            }
            catch
            {
                // and just in case that failed
                _cina.Dispose();
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
            get
            {
                return (linePower.Series.Count > 1 || lineVolts.Series.Count > 1) || lineCurrent.Series.Count > 1;
            }
            set
            {
                OnPropertyChanged("HasImportedData");
            }
        }

        void loadOn_timer_tick(object sender, EventArgs e)
        {
            try
            {
                getData();
            }
            catch (Exception ex)
            {
                lblStatus.Text = $"{DateTime.Now.ToShortTimeString()} {ex.Message}";
            }


            TimeSpan ts = DateTime.Now - (DateTime)_timer.Tag;
            if (ts.TotalSeconds > _load_on_duration_sec)
            {
                _timer.Stop();

                if (_rest_duration_sec > 0)
                {
                    _timer.Tick -= loadOn_timer_tick;
                    _timer.Tick += rest_timer_Tick;

                    connectLoad(false);

                    _timer.Start();
                }
                else
                {
                    runDone();
                }
            }


        }

        void rest_timer_Tick(object sender, EventArgs e)
        {
            DateTime dateTime = DateTime.Now;
            DateTime startTime = (DateTime)_timer.Tag;
            TimeSpan ts = DateTime.Now - startTime;

            getVoltage();

            if (ts.TotalSeconds > (_load_on_duration_sec + _rest_duration_sec))
            {
                _timer.Stop();
                runDone();
            }
        }

        void runDone()
        {
            if (_repeat_count-- > 0)
            {

                _timer.Tick -= rest_timer_Tick;
                _timer.Tick -= loadOn_timer_tick;
                _timer.Tick += loadOn_timer_tick;

                //_load_on_duration_sec += _load_on_duration_sec + _rest_duration_sec;
                TimeSpan etime = DateTime.Now - (DateTime)_timer.Tag;
                _load_on_duration_sec = etime.TotalSeconds + Convert.ToDouble(txtDuration.Text);
                connectLoad(true);

                _timer.Start();

            }
            else
            {
                _sw.Close();
                btnStart.Content = "Start";
            }
        }


        void getVoltage()
        {
            DateTime dateTime = DateTime.Now;
            TimeSpan ts = dateTime - (DateTime)_timer.Tag;

            bool ovf = false;
            float v = _cina.GetVoltage(ref ovf);
            voltage_colection.Add(new KeyValuePair<double, double>(ts.TotalMilliseconds, v));

            writeLineDataToFile(_sw, $"{dateTime},,{v},,{ts.Ticks}");

        }

        void getData()
        {
            DateTime dateTime = DateTime.Now;
            TimeSpan ts = dateTime - (DateTime)_timer.Tag;

            bool ovf = false;
            float v = _cina.GetVoltage(ref ovf);
            float i = _cina.GetCurrent();
            float p = v * i;

            voltage_colection.Add(new KeyValuePair<double, double>(ts.TotalMilliseconds, v));
            current_colection.Add(new KeyValuePair<double, double>(ts.TotalMilliseconds, i));
            power_collaction.Add(new KeyValuePair<double, double>(ts.TotalMilliseconds, p));

            double rate_fromStart = 0;
            if (power_collaction.Count > 0)
            {
                double deltaPower_fromStart = p - power_collaction[0].Value;
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

            addLineSeriesToCharts(title, map);

        }

        void addLineSeriesToCharts(string title, Dictionary<string, List<KeyValuePair<double, double>>> map)
        {
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
                double duration_ms = ts.TotalMilliseconds;

                double value = 0;
                string cell = r["Voltage(V)"].ToString();

                if( Double.TryParse(cell, out value) )
                    volts_list.Add(new KeyValuePair<double, double>(duration_ms, value));

                cell = r["Current(mA)"].ToString();
                if (Double.TryParse(cell, out value))
                    current_list.Add(new KeyValuePair<double, double>(duration_ms, value));

                cell = r["Power(mW)"].ToString();
                if (Double.TryParse(cell, out value))
                    power_list.Add(new KeyValuePair<double, double>(duration_ms, value));
            }

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
                DataColumn c = new DataColumn(cname);
                if (cname == "TimeStamp")
                    c = new DataColumn(cname, typeof(DateTime));
                else if (cname == "Duration")
                    c = new DataColumn(cname, typeof(long));
                table.Columns.Add(c);
            }

            // get the rows
            for (int i = 1; i < lines.Length; i++)
            {
                var fields = lines[i].Split(new[] { ',' });

                try
                {
                    table.Rows.Add(fields);
                }
                catch (Exception ex)
                {
                    lblStatus.Text = $"{DateTime.Now.ToShortTimeString()} {ex.Message}";
                }


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

        private void txtFileNameImput(object sender, TextCompositionEventArgs e)
        {
            string invalidChars = new String(System.IO.Path.GetInvalidFileNameChars());

            Regex regx = new Regex($"[{ Regex.Escape(invalidChars) }]");
            bool isInvalid = regx.IsMatch(e.Text);

            if (isInvalid)
            {
                e.Handled = true;
                return;
            }
            e.Handled = false;
        }

        string getValidFileName(string fileName)
        {
            return System.IO.Path.GetInvalidFileNameChars().Aggregate(
                fileName, (current, c) => current.Replace(c.ToString(), string.Empty));
        }

        void connectLoad(bool on_off = true)
        {
            try
            {
                _cina.SetACBusPin(0, on_off);
            }
            catch (Exception ex)
            {
                lblStatus.Text = $"{DateTime.Now.ToShortTimeString()} {ex.Message}";
            }

        }

        private void Button_StartClick(object sender, RoutedEventArgs e)
        {
            if (btnStart.Content.ToString() == "Start")
            {
                btnStart.Content = "Stop";

                _interval = Convert.ToInt32(txtInterval.Text);
                _load_on_duration_sec = Convert.ToDouble(txtDuration.Text);
                _rest_duration_sec = Convert.ToDouble(txtRest.Text);
                _repeat_count = Convert.ToInt32(txtRepeat.Text);

                voltage_colection = new ObservableCollection<KeyValuePair<double, double>>();
                current_colection = new ObservableCollection<KeyValuePair<double, double>>();
                power_collaction = new ObservableCollection<KeyValuePair<double, double>>();

                lineVolts.DataContext = voltage_colection;
                lineCurrent.DataContext = current_colection;
                linePower.DataContext = power_collaction;


                //((LineSeries)linePower.Series[0]).Title = "Test";
                //((LineSeries)linePower.Series[0]).ToolTip = "Name #SERIESNAME : X - #VALX{F2} , Y - #VALY{F2}";
                //var s = ((LineSeries)linePower.Series[0]).DataPointStyle.Resources;

                //string filename = $"BatteryProfile{DateTime.Now.ToString("MMdd_HHmm")}.csv";
                string filename = $"{getValidFileName(txtFileName.Text)}.csv";

                _sw = File.CreateText(filename);
                //_sw.AutoFlush = true;
                _sw.WriteLine($"TimeStamp,Power(mW),Voltage(V),Current(mA),Duration");

                _timer.Tick -= rest_timer_Tick;
                _timer.Tick += loadOn_timer_tick;
                _timer.Interval = new TimeSpan(0, 0, 0, 0, _interval);

                _timer.Tag = DateTime.Now;
                getVoltage();
                connectLoad(true);
                Thread.Sleep(_interval);
                _timer.Start();
            }
            else
            {
                _timer.Stop();
                _sw.Close();
                btnStart.Content = "Start";
            }
        }

    }
}
