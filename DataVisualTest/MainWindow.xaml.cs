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
        //private FTDI.FT_DEVICE_INFO_NODE[] devList = new FTDI.FT_DEVICE_INFO_NODE[100];

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


                voltage.Add(new KeyValuePair<double, double>(ts.TotalMinutes, v));
                if (voltage.Count > 1)
                {
                    if (i > 0)
                        current.Add(new KeyValuePair<double, double>(ts.TotalMinutes, i));
                    if (p > 0)
                        power.Add(new KeyValuePair<double, double>(ts.TotalMinutes, p));
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


                writeLineDataToFile(_sw, $"{dateTime},{p},{v},{i}");
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


                timer.Interval = new TimeSpan(0, 0, 0, 0, Int32.Parse(txtInterval.Text));

                timer.Tag = DateTime.Now;
                timer.Start();

                string filename = $"BatteryProfile{DateTime.Now.ToString("MMdd_HHmm")}.csv";
                _sw = File.CreateText(filename);
                _sw.AutoFlush = true;
                _sw.WriteLine($"TimeStamp,Power(mW),Voltage(V),Current(mA)");

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
