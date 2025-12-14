using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Windows.Forms;
using ZedGraph;

namespace MissionPlanner.Controls
{
    public partial class ConnectionStats : UserControl
    {
        private readonly MAVLinkInterface _mavlink;
        private CompositeDisposable _subscriptionsDisposable;
        private RollingPointPairList _rxBytesPerSecList;
        private RollingPointPairList _txBytesPerSecList;
        private LineItem _rxCurve;
        private LineItem _txCurve;
        private DateTime _graphStartTime;

        public ConnectionStats(MAVLinkInterface comPort)
            : this()
        {
            _mavlink = comPort;

            chk_mavlink2.Checked = _mavlink.MAV.mavlinkv2;

            chk_signing.Checked = _mavlink.MAV.signing;

            this.Load += ConnectionStats_Load;
            this.Disposed += (sender, e) => StopUpdates();
        }

        public ConnectionStats()
        {
            InitializeComponent();
        }

        void ConnectionStats_Load(object sender, EventArgs e)
        {
            _subscriptionsDisposable = new CompositeDisposable();

            txt_BytesPerSecondRx.Text = "";
            txt_BytesPerSecondSent.Text = "";
            txt_BytesReceived.Text = "";
            txt_BytesSent.Text = "";
            txt_LinkQuality.Text = "";
            txt_MaxPacketInterval.Text = "";
            txt_PacketsLost.Text = "";
            txt_PacketsPerSecond.Text = "";
            txt_PacketsRx.Text = "";

            // Setup the bytes/sec graph
            SetupBytesPerSecGraph();

            var packetsReceivedCount = _mavlink.WhenPacketReceived.Scan(0, (x, y) => x + y);
            var packetsLostCount = _mavlink.WhenPacketLost.Scan(0, (x, y) => x + y);

            var bytesReceivedEverySecond = _mavlink.BytesReceived
                .Buffer(TimeSpan.FromSeconds(1))
                .Select(bytes => bytes.Sum());

            var bytesSentEverySecond = _mavlink.BytesSent
                .Buffer(TimeSpan.FromSeconds(1))
                .Select(bytes => bytes.Sum());

            var subscriptions = new List<IDisposable>
            {
                // Total number of packets received
                // but only update the text box at 4Hz
                packetsReceivedCount
                    .Sample(TimeSpan.FromMilliseconds(250))
                    .SubscribeForTextUpdates(txt_PacketsRx),
                packetsLostCount
                    .Sample(TimeSpan.FromMilliseconds(250))
                    .SubscribeForTextUpdates(txt_PacketsLost),

                // Packets per second = total number of packets received over the
                // last 3 seconds divided by three
                // Do that every second
                _mavlink.WhenPacketReceived
                    .Buffer(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1))
                    .Select(xs => xs.Sum()/3.0)
                    .ObserveOn(SynchronizationContext.Current)
                    .Subscribe(x => this.txt_PacketsPerSecond.Text = x.ToString("0")),

                // Link quality is a percentage of the number of good packets received
                // to the number of packets missed (detected by mavlink seq no.)
                // Calculated as an average over the last 3 seconds (non weighted)
                // Calculated every second
                CombineWithDefault(_mavlink.WhenPacketReceived, _mavlink.WhenPacketLost, Tuple.Create)
                    .Buffer(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1))
                    .Select(CalculateAverage)
                    .ObserveOn(SynchronizationContext.Current)
                    .Subscribe(x => this.txt_LinkQuality.Text = x.ToString("00%")),

                // Bytes per second is the average number of bytes received every second
                // sampled for the last 3 seconds
                // updated every second
                bytesReceivedEverySecond
                    .Buffer(3, 1)
                    .Select(xs => (int) xs.Average())
                    .Select(ToHumanReadableByteCount)
                    .SubscribeForTextUpdates(txt_BytesPerSecondRx),

                // Total bytes received - just count them up,
                // but only update the text box at 4Hz so as not to swamp the UI thread
                // Also use a human friendly version e.g '1.3K' not 1345
                _mavlink.BytesReceived
                    .Scan(0, (x, y) => x + y)
                    .Sample(TimeSpan.FromMilliseconds(250))
                    .Select(ToHumanReadableByteCount)
                    .SubscribeForTextUpdates(txt_BytesReceived),
                _mavlink.BytesSent
                    .Scan(0, (x, y) => x + y)
                    .Sample(TimeSpan.FromMilliseconds(250))
                    .Select(ToHumanReadableByteCount)
                    .SubscribeForTextUpdates(txt_BytesSent),
                bytesSentEverySecond
                    .Buffer(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1))
                    .Select(xs => xs.Any() ? xs.Average() : 0)
                    .Select(x => ToHumanReadableByteCount((int) x))
                    .SubscribeForTextUpdates(txt_BytesPerSecondSent),

//                                        Observable.Interval(TimeSpan.FromSeconds(1))
//                                            .Scan(TimeSpan.Zero, (a, _) => a.Add(TimeSpan.FromSeconds(1)))
//                                            .ObserveOn(SynchronizationContext.Current)
//                                            .Subscribe(ts => this.txt_TimeConnected.Text = ts.ToString()),

                // The maximum length of time between reception of good packets
                // evaluated continuously
                _mavlink.WhenPacketReceived
                    .TimeInterval()
                    .Select(x => x.Interval.Ticks)
                    .Scan(0L, Math.Max)
                    .Select(TimeSpan.FromTicks)
                    .Select(ts => ts.Milliseconds)
                    .ObserveOn(SynchronizationContext.Current)
                    .SubscribeForTextUpdates(txt_MaxPacketInterval),

                // Update graph with RX bytes per second
                bytesReceivedEverySecond
                    .ObserveOn(SynchronizationContext.Current)
                    .Subscribe(bytesPerSec => UpdateGraph(_rxBytesPerSecList, bytesPerSec)),

                // Update graph with TX bytes per second
                bytesSentEverySecond
                    .ObserveOn(SynchronizationContext.Current)
                    .Subscribe(bytesPerSec => UpdateGraph(_txBytesPerSecList, bytesPerSec)),
            };

            subscriptions.ForEach(d => _subscriptionsDisposable.Add(d));
        }

        private void SetupBytesPerSecGraph()
        {
            _graphStartTime = DateTime.Now;
            _rxBytesPerSecList = new RollingPointPairList(60); // Keep 60 seconds of data
            _txBytesPerSecList = new RollingPointPairList(60);

            GraphPane myPane = zedGraphBytesPerSec.GraphPane;

            // Clear any existing curves
            myPane.CurveList.Clear();

            // Set titles
            myPane.Title.Text = "Link Bandwidth";
            myPane.Title.FontSpec.Size = 10;
            myPane.XAxis.Title.Text = "Time (s)";
            myPane.XAxis.Title.FontSpec.Size = 9;
            myPane.YAxis.Title.Text = "Bytes/s";
            myPane.YAxis.Title.FontSpec.Size = 9;

            // Configure margins for compact display
            myPane.Margin.All = 0;
            myPane.Legend.IsVisible = true;
            myPane.Legend.FontSpec.Size = 8;
            myPane.Legend.Position = LegendPos.TopCenter;

            // Show grid
            myPane.XAxis.MajorGrid.IsVisible = true;
            myPane.YAxis.MajorGrid.IsVisible = true;

            // Set initial scale
            myPane.XAxis.Scale.Min = 0;
            myPane.XAxis.Scale.Max = 30;
            myPane.YAxis.Scale.Min = 0;

            // Add RX curve (green)
            _rxCurve = myPane.AddCurve("RX", _rxBytesPerSecList, Color.LimeGreen, SymbolType.None);
            _rxCurve.Line.Width = 2;

            // Add TX curve (orange)
            _txCurve = myPane.AddCurve("TX", _txBytesPerSecList, Color.Orange, SymbolType.None);
            _txCurve.Line.Width = 2;

            zedGraphBytesPerSec.AxisChange();
        }

        private void UpdateGraph(RollingPointPairList list, int bytesPerSec)
        {
            if (list == null || zedGraphBytesPerSec == null || zedGraphBytesPerSec.IsDisposed)
                return;

            double time = (DateTime.Now - _graphStartTime).TotalSeconds;
            list.Add(time, bytesPerSec);

            // Keep X axis rolling
            Scale xScale = zedGraphBytesPerSec.GraphPane.XAxis.Scale;
            if (time > xScale.Max - 5)
            {
                xScale.Max = time + 5;
                xScale.Min = xScale.Max - 30;
            }

            // Auto-scale Y axis
            zedGraphBytesPerSec.AxisChange();
            zedGraphBytesPerSec.Invalidate();
        }

        public void StopUpdates()
        {
            _subscriptionsDisposable.Dispose();
        }

        private static IObservable<TResult> CombineWithDefault<TSource, TResult>(IObservable<TSource> first,
            Subject<TSource> second, Func<TSource, TSource, TResult> resultSelector)
        {
            return Observable.Defer(() =>
            {
                var foo = new Subject<TResult>();

                first.Select(x => resultSelector(x, default(TSource))).Subscribe(foo);
                second.Select(x => resultSelector(default(TSource), x)).Subscribe(foo);

                return foo;
            });
        }

        private static double CalculateAverage(IList<Tuple<int, int>> xs)
        {
            var packetsReceived = xs.Sum(t => t.Item1);
            var packetsLost = xs.Sum(t => t.Item2);

            return packetsReceived / (packetsReceived + (double)packetsLost);
        }

        public static string ToHumanReadableByteCount(int i)
        {
            if (i > 1024 * 1024 * 1024)
                return string.Format("{0:0.00}GB", i / (float)(1024 * 1024 * 1024));
            if (i > 1024 * 1024)
                return string.Format("{0:0.00}MB", i / (float)(1024 * 1024));
            if (i > 1024)
                return string.Format("{0:0.00}KB", i / (float)1024);
            return string.Format("{0:####}B", i);
        }

        private void chk_mavlink2_CheckedChanged(object sender, EventArgs e)
        {
            _mavlink.MAV.mavlinkv2 = chk_mavlink2.Checked;
        }

        private void chk_signing_CheckedChanged(object sender, EventArgs e)
        {
            _mavlink.MAV.signing = chk_signing.Checked;
        }

        private void but_reset_Click(object sender, EventArgs e)
        {
            StopUpdates();
            ConnectionStats_Load(this, null);
        }
    }


    public static class CompositeDisposableEx
    {
        public static IDisposable SubscribeForTextUpdates<T>(this IObservable<T> source, TextBox txtBox)
        {
            return source
                .ObserveOn(SynchronizationContext.Current)
                .Subscribe(x => txtBox.Text = x.ToString());
        }
    }
}