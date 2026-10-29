using MissionPlanner.Comms;
using MissionPlanner.Utilities;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Windows.Forms;

namespace MissionPlanner.Controls
{
    public partial class SerialOutputPass : Form
    {
        // Track active connections with their listeners
        private List<ConnectionInfo> activeConnections = new List<ConnectionInfo>();

        private class ConnectionInfo
        {
            public string Type { get; set; }
            public string Direction { get; set; }
            public string Address { get; set; }
            public bool WriteAccess { get; set; }
            public MAVLinkInterface ComPort { get; set; }
            public MAVLinkInterface.Mirror Mirror { get; set; }
            public TcpListener Listener { get; set; }
            public int RowIndex { get; set; }
        }

        public SerialOutputPass()
        {
            InitializeComponent();

            MissionPlanner.Utilities.Tracking.AddPage(this.GetType().ToString(), this.Text);
        }

        private void SerialOutputPass_Load(object sender, EventArgs e)
        {
            // Set default selections
            cmbType.SelectedIndex = 0; // TCP
            cmbDirection.SelectedIndex = 1; // Outbound

            // Refresh the grid with any existing mirrors
            RefreshConnectionsList();

            Log("MAVLink Output ready");
        }

        private void cmbType_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateUIForConnectionType();
        }

        private void cmbDirection_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateUIForConnectionType();
        }

        private void UpdateUIForConnectionType()
        {
            string type = cmbType.SelectedItem?.ToString() ?? "TCP";
            string direction = cmbDirection.SelectedItem?.ToString() ?? "Outbound";

            switch (type)
            {
                case "Serial":
                    lblDirection.Visible = false;
                    cmbDirection.Visible = false;
                    lblPort.Text = "COM Port:";
                    txtPort.Text = "";
                    lblExtra.Text = "Baud Rate:";
                    txtExtra.Text = "115200";
                    break;

                case "TCP":
                case "UDP":
                    lblDirection.Visible = true;
                    cmbDirection.Visible = true;

                    if (direction == "Inbound")
                    {
                        lblPort.Text = "Listen Port:";
                        txtPort.Text = "14550";
                        lblExtra.Text = "";
                        txtExtra.Text = "";
                        txtExtra.Visible = false;
                        lblExtra.Visible = false;
                    }
                    else // Outbound
                    {
                        lblPort.Text = "Port:";
                        txtPort.Text = "14550";
                        lblExtra.Text = "Host:";
                        txtExtra.Text = "127.0.0.1";
                        txtExtra.Visible = true;
                        lblExtra.Visible = true;
                    }
                    break;
            }
        }

        private void btnAdd_Click(object sender, EventArgs e)
        {
            try
            {
                string type = cmbType.SelectedItem?.ToString();
                string direction = cmbDirection.SelectedItem?.ToString() ?? "-";
                bool writeAccess = chkWriteAccess.Checked;

                if (string.IsNullOrEmpty(type))
                {
                    CustomMessageBox.Show("Please select a connection type.");
                    return;
                }

                MAVLinkInterface.Mirror mirror = new MAVLinkInterface.Mirror();
                mirror.MirrorStreamWrite = writeAccess;
                string address = "";
                TcpListener listener = null;

                switch (type)
                {
                    case "TCP":
                        if (direction == "Inbound")
                        {
                            int port;
                            if (!int.TryParse(txtPort.Text, out port))
                            {
                                CustomMessageBox.Show("Invalid port number.");
                                return;
                            }

                            var tcp = new TcpSerial();
                            mirror.MirrorStream = tcp;
                            listener = new TcpListener(IPAddress.Any, port);
                            listener.Start(0);
                            listener.BeginAcceptTcpClient(DoAcceptTcpClientCallback, (listener, tcp));

                            address = $":{port}";
                            Log($"TCP listener started on port {port}");
                        }
                        else // Outbound
                        {
                            string host = txtExtra.Text.Trim();
                            string port = txtPort.Text.Trim();

                            if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(port))
                            {
                                CustomMessageBox.Show("Please enter host and port.");
                                return;
                            }

                            mirror.MirrorStream = new TcpSerial()
                            {
                                retrys = 999999,
                                autoReconnect = true,
                                Host = host,
                                Port = port,
                                ConfigRef = "SerialOutputPassTCP"
                            };
                            mirror.MirrorStream.Open();

                            address = $"{host}:{port}";
                            Log($"TCP connection established to {address}");
                        }
                        break;

                    case "UDP":
                        if (direction == "Inbound")
                        {
                            int port;
                            if (!int.TryParse(txtPort.Text, out port))
                            {
                                CustomMessageBox.Show("Invalid port number.");
                                return;
                            }

                            var udp = new UdpSerial()
                            {
                                ConfigRef = "SerialOutputPassUDP",
                                Port = port.ToString()
                            };
                            udp.client = new UdpClient(port);
                            udp.IsOpen = true;
                            mirror.MirrorStream = udp;

                            address = $":{port}";
                            Log($"UDP listener started on port {port}");
                        }
                        else // Outbound
                        {
                            string host = txtExtra.Text.Trim();
                            int port;
                            if (!int.TryParse(txtPort.Text, out port) || string.IsNullOrEmpty(host))
                            {
                                CustomMessageBox.Show("Please enter valid host and port.");
                                return;
                            }

                            var udp = new UdpSerialConnect()
                            {
                                ConfigRef = "SerialOutputPassUDPCL"
                            };
                            udp.hostEndPoint = new IPEndPoint(IPAddress.Parse(host), port);
                            udp.client = new UdpClient();
                            udp.IsOpen = true;
                            mirror.MirrorStream = udp;

                            address = $"{host}:{port}";
                            Log($"UDP connection established to {address}");
                        }
                        break;

                    case "Serial":
                        string comPort = txtPort.Text.Trim();
                        int baudRate;
                        if (!int.TryParse(txtExtra.Text, out baudRate))
                        {
                            CustomMessageBox.Show("Invalid baud rate.");
                            return;
                        }

                        if (string.IsNullOrEmpty(comPort))
                        {
                            CustomMessageBox.Show("Please enter a COM port name.");
                            return;
                        }

                        mirror.MirrorStream = new SerialPort();
                        mirror.MirrorStream.PortName = comPort;
                        mirror.MirrorStream.BaudRate = baudRate;
                        mirror.MirrorStream.Open();
                        direction = "-";

                        address = $"{comPort}@{baudRate}";
                        Log($"Serial port {address} opened");
                        break;

                    default:
                        CustomMessageBox.Show("Unknown connection type.");
                        return;
                }

                // AddMirror replaces the list under the interface's lock: the reader thread enumerates
                // Mirrors, and the legacy MirrorStream setter may be replacing it from another thread
                var comPortInterface = MainV2.comPort;
                comPortInterface.AddMirror(mirror);

                // Add to our tracking list
                var connInfo = new ConnectionInfo
                {
                    ComPort = comPortInterface,
                    Type = type,
                    Direction = direction,
                    Address = address,
                    WriteAccess = writeAccess,
                    Mirror = mirror,
                    Listener = listener
                };

                // Add row to grid
                int rowIndex = dgvConnections.Rows.Add(
                    type,
                    direction,
                    address,
                    "Connected",
                    writeAccess
                );
                connInfo.RowIndex = rowIndex;
                dgvConnections.Rows[rowIndex].Tag = connInfo;

                activeConnections.Add(connInfo);
            }
            catch (Exception ex)
            {
                Log($"Error: {ex.Message}");
                CustomMessageBox.Show($"Failed to create connection: {ex.Message}");
            }
        }

        private void DoAcceptTcpClientCallback(IAsyncResult ar)
        {
            // the listener that accepted the client and the stream the client is handed to
            var state = (ValueTuple<TcpListener, TcpSerial>)ar.AsyncState;
            TcpListener listener = state.Item1;
            TcpSerial tcp = state.Item2;

            TcpClient client = null;
            try
            {
                client = listener.EndAcceptTcpClient(ar);

                var previous = tcp.client;
                tcp.client = client;
                previous?.Close();

                // Log on UI thread. The form may have closed (the mirror keeps running), and a failed
                // log must not reach the catch below, which treats its exceptions as a stopped listener
                try
                {
                    var remote = client.Client.RemoteEndPoint?.ToString();
                    if (!IsDisposed && IsHandleCreated)
                    {
                        BeginInvoke((Action)(() =>
                        {
                            Log($"TCP client connected from {remote}");
                        }));
                    }
                }
                catch
                {
                }

                // Continue accepting more clients
                listener.BeginAcceptTcpClient(DoAcceptTcpClientCallback, state);
            }
            catch (Exception ex) when (ex is ObjectDisposedException || ex is SocketException || ex is InvalidOperationException)
            {
                // listener was stopped. StopConnection stops the listener before closing the stream, so a client
                // accepted while stopping may have been set after the close and has to be closed here
                client?.Close();
            }
            catch (Exception ex)
            {
                if (!IsDisposed && IsHandleCreated)
                {
                    BeginInvoke((Action)(() =>
                    {
                        Log($"TCP accept error: {ex.Message}");
                    }));
                }
            }
        }

        private void dgvConnections_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            // Check if Stop button was clicked
            if (e.ColumnIndex == colStop.Index && e.RowIndex >= 0)
            {
                StopConnection(e.RowIndex);
            }
        }

        private void StopConnection(int rowIndex)
        {
            try
            {
                var row = dgvConnections.Rows[rowIndex];
                var connInfo = row.Tag as ConnectionInfo;

                if (connInfo != null)
                {
                    // Stop listener if exists
                    if (connInfo.Listener != null)
                    {
                        connInfo.Listener.Stop();
                        connInfo.Listener = null;
                    }

                    // Close stream
                    if (connInfo.Mirror?.MirrorStream != null)
                    {
                        try
                        {
                            connInfo.Mirror.MirrorStream.Close();
                        }
                        catch { }
                    }

                    // Remove from the interface it was added to
                    if (connInfo.Mirror != null)
                    {
                        (connInfo.ComPort ?? MainV2.comPort).RemoveMirror(connInfo.Mirror);
                    }

                    activeConnections.Remove(connInfo);

                    Log($"Stopped {connInfo.Type} connection to {connInfo.Address}");
                }

                // Remove row from grid
                dgvConnections.Rows.RemoveAt(rowIndex);

                // Update row indices for remaining connections
                for (int i = 0; i < dgvConnections.Rows.Count; i++)
                {
                    var info = dgvConnections.Rows[i].Tag as ConnectionInfo;
                    if (info != null)
                    {
                        info.RowIndex = i;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error stopping connection: {ex.Message}");
            }
        }

        private void RefreshConnectionsList()
        {
            // Show any existing mirrors from MainV2.comPort.Mirrors
            foreach (var mirror in MainV2.comPort.Mirrors)
            {
                if (mirror.MirrorStream != null)
                {
                    string type = "Unknown";
                    string direction = "-";
                    string address = "";

                    if (mirror.MirrorStream is TcpSerial)
                    {
                        type = "TCP";
                        var tcp = mirror.MirrorStream as TcpSerial;
                        address = $"{tcp.Host}:{tcp.Port}";
                        direction = string.IsNullOrEmpty(tcp.Host) ? "Inbound" : "Outbound";
                    }
                    else if (mirror.MirrorStream is UdpSerial)
                    {
                        type = "UDP";
                        direction = "Inbound";
                        var udp = mirror.MirrorStream as UdpSerial;
                        address = $":{udp.Port}";
                    }
                    else if (mirror.MirrorStream is UdpSerialConnect)
                    {
                        type = "UDP";
                        direction = "Outbound";
                        var udp = mirror.MirrorStream as UdpSerialConnect;
                        address = udp.hostEndPoint?.ToString() ?? "";
                    }
                    else if (mirror.MirrorStream is SerialPort)
                    {
                        type = "Serial";
                        var serial = mirror.MirrorStream as SerialPort;
                        address = $"{serial.PortName}@{serial.BaudRate}";
                    }

                    string status = mirror.MirrorStream.IsOpen ? "Connected" : "Disconnected";

                    int rowIndex = dgvConnections.Rows.Add(
                        type,
                        direction,
                        address,
                        status,
                        mirror.MirrorStreamWrite
                    );

                    var connInfo = new ConnectionInfo
                    {
                        ComPort = MainV2.comPort,
                        Type = type,
                        Direction = direction,
                        Address = address,
                        WriteAccess = mirror.MirrorStreamWrite,
                        Mirror = mirror,
                        RowIndex = rowIndex
                    };
                    dgvConnections.Rows[rowIndex].Tag = connInfo;
                    activeConnections.Add(connInfo);
                }
            }
        }

        private void Log(string message)
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action)(() => Log(message)));
                return;
            }

            txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\r\n");
        }

        private void btnClear_Click(object sender, EventArgs e)
        {
            txtLog.Clear();
        }

        private void SerialOutputPass_FormClosing(object sender, FormClosingEventArgs e)
        {
            // Note: We don't stop connections on form close
            // They should continue running in the background
            // User can reopen the form to see/manage them
        }
    }
}
