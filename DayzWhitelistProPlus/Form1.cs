using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.Odbc;
using System.Drawing;
using System.Linq;
using MySql.Data.MySqlClient;
using Microsoft.VisualBasic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using BattleNET;

namespace DayzWhitelistProPlus
{
    public partial class frmMain : Form
    {
        // Load rcon credentials
        public static string ip       = Properties.Settings.Default.rconIP;
        public static int port        = Properties.Settings.Default.rconPort;
        public static string password = Properties.Settings.Default.rconPass;

        // Load database credentials
        public static String dbHost     = Properties.Settings.Default.dbHost;
        public static String dbUser     = Properties.Settings.Default.dbUser;
        public static String dbPort     = Properties.Settings.Default.dbPort.ToString();
        public static String dbDatabase = Properties.Settings.Default.dbDatabase;
        public static String dbPassword = Properties.Settings.Default.dbPass;

        static IBattleNET b;

        private Timer keepAliveTimer;
        Color bgcolor;

        private static bool isConnected = false;

        /* Options: need loading from config file eventually */
        private static bool showChat = true;
        private static bool showWelcomeMsg = true;
        private static bool showStartupMsg = true;
        private static bool whiteListEnabled = true;
        private static bool addNewPlayersWhenDisabled = false;
        private static String serverURL = "http://uk8008.co.uk/";
                
        delegate void DisconnectCallback(string text);
        delegate void DumpMessageCallback(string text);
        delegate void printLineInConsoleCallback(string text);
        
        public frmMain()
        {
            InitializeComponent();
        }

        private void frmMain_Load(object sender, EventArgs e)
        {
            if (Properties.Settings.Default.firstTimeOpening)
            {
                frmSettings frmS = new frmSettings(this);
                frmS.ShowDialog();

                Properties.Settings.Default.Upgrade();
                Properties.Settings.Default.firstTimeOpening = false;
                Properties.Settings.Default.Save();
            }
        }

        /* DISCONNECT EVENT */
        private void Disconnected(BattlEyeDisconnectEventArgs args)
        {
            mnuConnect.Enabled = false;
            doDisconnect(args.Message);
        }

        private void doDisconnect(String msg)
        {
            if (this.rtbDisplay.InvokeRequired)
            {
                DisconnectCallback d = new DisconnectCallback(doDisconnect);
                this.Invoke(d, new object[] { msg + "\n" });
            }
            else
            {
                this.rtbDisplay.AppendText(msg + "\n", Color.Gray);
            }
        }

        /* DUMP MESSAGE EVENT */
        private void DumpMessage(BattlEyeMessageEventArgs args)
        {
            doDumpMessage(args.Message);
        }

        private void doDumpMessage(String msg)
        {
            setTextColor(msg);

            if (this.rtbDisplay.InvokeRequired)
            {
                DumpMessageCallback d = new DumpMessageCallback(doDumpMessage);
                this.Invoke(d, new object[] { msg });
            }
            else
            {
                // Show chat messages if enabled
                if (showChat == true)
                {
                    this.rtbDisplay.AppendText(msg, bgcolor);
                }

                try
                {
                    Match matchString;

                    // Grab the user data if it matches our regular expresion - Thanks to mmmmk for this Regex!
                    matchString = Regex.Match(msg, @"Player #(?<player_id>[0-9]{1,3})\s(?<user>.+) - GUID: (?<guid>.+)\W\D\S", RegexOptions.IgnoreCase);
                    if (matchString.Success)
                    {
                        // new clien tobj
                        DayzClient client = new DayzClient();

                        client.GUID = matchString.Groups["guid"].Value.Trim();
                        client.playerNo = Convert.ToInt32(matchString.Groups["player_id"].Value);
                        client.UserName = matchString.Groups["user"].Value;

                        // did we get a valid result? verify
                        if (client.GUID != null && client.UserName != null)
                        {
                            if (VerifyWhiteList(client) == false)
                            {
                                // user is not white listed kick and send message
                                KickPlayer(client);
                                //addPlayer(client);

                                // log event
                                client.logType = DayzClient.LogTypes.Kick;
                                LogPlayer(client);
                            }
                            else
                            {
                                // display welcome message
                                WelcomeMessage(client);

                                // log event;
                                client.logType = DayzClient.LogTypes.Success;
                                LogPlayer(client);
                            }
                        }

                        // destroy client obj
                        client = null;

                        //Console.WriteLine(matchString.Groups["player_id"].Value + "*" + matchString.Groups["guid"].Value + "*" + matchString.Groups["user"].Value);
                    }
                }
                catch (Exception ex)
                {
                    // do nothing
                }

            }
            
        }

        /* BATTLEYE FUNCTIONS */
        
        // Sends an empty packet to keep the connection alive
        private void sendKeepAlivePacket(object sender, EventArgs e)
        {
            b.SendCommandPacket("");
        }

        /* WHITELIST FUNCTIONS */

        private static bool VerifyWhiteList(DayzClient client)
        {
            bool returnVal = false;

            string connStr = string.Format("server={0};user={1};database={2};port={3};password={4};", dbHost, dbUser, dbDatabase, dbPort, dbPassword);

            MySqlConnection conn = new MySqlConnection(connStr);
            MySqlCommand cmd = new MySqlCommand();
            MySqlDataReader rdr = null;

            try
            {
                conn.Open();
                cmd.Connection = conn;
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandText = "proc_CheckWhiteList";

                cmd.Parameters.Add(new MySqlParameter("p_guid", client.GUID));

                rdr = cmd.ExecuteReader();

                if (rdr.HasRows == true)
                {
                    returnVal = true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
            finally
            {
                rdr.Close();
                conn.Close();
                rdr = null;
                conn = null;
                cmd = null;
            }
            return returnVal;
        }

        private void WelcomeMessage(DayzClient client)
        {
            if (showWelcomeMsg == true)
            {
                b.SendCommandPacket(EBattlEyeCommand.Say, string.Format("-1 Welcome: {0}", client.UserName));
            }

            this.rtbDisplay.AppendText(string.Format(" Verified Player {0}: {1} - {2}\n", client.playerNo.ToString(), client.GUID.ToString(), client.UserName.ToString()));
        }
       
        private void KickPlayer(DayzClient client)
        {
            if (whiteListEnabled == true)
            {
                b.SendCommandPacket(EBattlEyeCommand.Kick, string.Format(@"{0} not whitelisted! Signup @ {1}", client.playerNo.ToString(), serverURL));
                this.rtbDisplay.AppendText(string.Format(" Kicked Player {0} : {1} - {2}\n", client.playerNo.ToString(), client.GUID.ToString(), client.UserName.ToString()));
            }
            else if (addNewPlayersWhenDisabled == true)
            {
                addPlayer(client);
            }
        }

        public void addPlayer(DayzClient client)
        {
            // call insert to log function
            string connStr = string.Format("server={0};user={1};database={2};port={3};password={4};", dbHost, dbUser, dbDatabase, dbPort, dbPassword);

            MySqlConnection conn = new MySqlConnection(connStr);
            MySqlCommand cmd = new MySqlCommand();

            string queryString = string.Format("call proc_CheckWhiteList('{0}')", client.GUID);

            try
            {
                conn.Open();
                cmd.Connection = conn;
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandText = "proc_AddWhiteListed";

                cmd.Parameters.Add(new MySqlParameter("p_name", client.UserName));
                cmd.Parameters.Add(new MySqlParameter("p_email", client.email));
                cmd.Parameters.Add(new MySqlParameter("p_GUID", client.GUID));

                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                this.rtbDisplay.AppendText(ex.ToString());
            }
            finally
            {
                conn.Close();
                conn = null;
                cmd = null;
            }
            this.rtbDisplay.AppendText(string.Format(" Added Player to whitelist {0} : {1} - {2}\n", client.playerNo.ToString(), client.GUID.ToString(), client.UserName.ToString()));
        }

        public void removePlayer(DayzClient client)
        {
            // call insert to log function
            string connStr = string.Format("server={0};user={1};database={2};port={3};password={4};", dbHost, dbUser, dbDatabase, dbPort, dbPassword);

            MySqlConnection conn = new MySqlConnection(connStr);
            MySqlCommand cmd = new MySqlCommand();

            string queryString = string.Format("call proc_CheckWhiteList('{0}')", client.GUID);

            try
            {
                conn.Open();
                cmd.Connection = conn;
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandText = "proc_SetWhitelistedStatus";

                cmd.Parameters.Add(new MySqlParameter("p_GUID", client.GUID)); 
                cmd.Parameters.Add(new MySqlParameter("p_whitelisted", 0));

                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                this.rtbDisplay.AppendText(ex.ToString());
            }
            finally
            {
                conn.Close();
                conn = null;
                cmd = null;
            }
            this.rtbDisplay.AppendText(string.Format(" Removed Player from whitelist - {0}\n", client.GUID.ToString()));
        }

        private void LogPlayer(DayzClient client)
        {
            // call insert to log function
            string connStr = string.Format("server={0};user={1};database={2};port={3};password={4};", dbHost, dbUser, dbDatabase, dbPort, dbPassword);

            MySqlConnection conn = new MySqlConnection(connStr);
            MySqlCommand cmd = new MySqlCommand();

            string queryString = string.Format("call proc_CheckWhiteList('{0}')", client.GUID);

            try
            {
                conn.Open();
                cmd.Connection = conn;
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandText = "proc_LogWhiteList";

                cmd.Parameters.Add(new MySqlParameter("p_name", client.UserName));
                cmd.Parameters.Add(new MySqlParameter("p_GUID", client.GUID));
                cmd.Parameters.Add(new MySqlParameter("p_logtype", Convert.ToInt32(client.logType)));

                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                this.rtbDisplay.AppendText(ex.ToString());
            }
            finally
            {
                conn.Close();
                conn = null;
                cmd = null;
            }
        }

        /* MISC FUNCTIONS */

        // updates 'bgcolor' depending on type of text being recieved
        private void setTextColor(String msg)
        {
            String text = msg.Substring(0, 5);

            switch (text)
            {
                case "(Side":
                    bgcolor = Color.Cyan;
                    break;
                case "RCon ":
                    bgcolor = Color.Red;
                    break;
                case "(Dire":
                    bgcolor = Color.White;
                    break;
                case "(Vehi":
                    bgcolor = Color.Yellow;
                    break;
                default:
                    bgcolor = Color.Gray;
                    break;
            }
        }
        // Turning whitelist On/Off
        private void mnuWhitelistActiveChange_Click(object sender, EventArgs e)
        {
            if (whiteListEnabled == false)
            {
                whiteListEnabled = true;
                mnuWhitelistActiveChange.Text = "Turn Whitelist Off";
                this.rtbDisplay.AppendText("Whitelist turned on.", Color.White);
            }
            else
            {
                whiteListEnabled = false;
                mnuWhitelistActiveChange.Text = "Turn Whitelist On";
                this.rtbDisplay.AppendText("Whitelist turned off.", Color.White);
            }
        }
        // Turning welcome message On/Off
        private void mnuWelcomeChange_Click(object sender, EventArgs e)
        {
            if (showWelcomeMsg == false)
            {
                showWelcomeMsg = true;
                mnuWelcomeChange.Text = "Turn Welcome Off";
                this.rtbDisplay.AppendText("Welcome turned on.", Color.White);
            }
            else
            {
                showWelcomeMsg = false;
                mnuWelcomeChange.Text = "Turn Welcome On";
                this.rtbDisplay.AppendText("Welcome turned off.", Color.White);
            }
        }
        // Turning chat display On/Off
        private void mnuChatChange_Click(object sender, EventArgs e)
        {
            if (showChat == false)
            {
                showChat = true;
                mnuChatChange.Text = "Turn Chat Off";
                this.rtbDisplay.AppendText("Chat turned on.", Color.White);
            }
            else
            {
                showChat = false;
                mnuChatChange.Text = "Turn Chat On";
                this.rtbDisplay.AppendText("Chat turned off.", Color.White);
            }
        }
        // Whether or not to add new players to whitelist when the whitelist is disabled
        private void mnuAddNewPlayersChange_Click(object sender, EventArgs e)
        {
            if (addNewPlayersWhenDisabled == false)
            {
                addNewPlayersWhenDisabled = true;
                mnuAddNewPlayersChange.Text = "Don't add new players when whitelist is diabled";
                this.rtbDisplay.AppendText("New players will be automatically added to whitelist.", Color.White);
            }
            else
            {
                addNewPlayersWhenDisabled = false;
                mnuAddNewPlayersChange.Text = "Add new players when whitelist is diabled";
                this.rtbDisplay.AppendText("New players will NOT be automatically added to whitelist.", Color.White);
            }
        }

        /* Loading of other forms */

        // Show settings
        private void mnuShowSettings(object sender, EventArgs e)
        {
            frmSettings frmSettings = new frmSettings(this);
            frmSettings.ShowDialog(this);
        }
        // Shows "Add GUID" form
        private void mnuAddGUID_Click(object sender, EventArgs e)
        {
            frmAddGUID frmAdd = new frmAddGUID(this);
            frmAdd.ShowDialog(this);
        }
        // Shows "Remove GUID" form
        private void mnuRemoveGUID_Click(object sender, EventArgs e)
        {
            frmRemoveGUID frmRemove = new frmRemoveGUID(this);
            frmRemove.ShowDialog(this);
        }

        private void mnuConnect_Click(object sender, EventArgs e)
        {
            if (isConnected == false)
            {   
                BattlEyeLoginCredentials logcred = new BattlEyeLoginCredentials { Host = ip, Password = password, Port = Convert.ToInt32(port) };
                b = new BattlEyeClient(logcred);
                keepAliveTimer = new Timer();
                keepAliveTimer.Tick += new EventHandler(sendKeepAlivePacket);
                keepAliveTimer.Interval = 30000; // in miliseconds

                rtbDisplay.AppendText("\n Connecting...\n");

                // make the connection
                b.MessageReceivedEvent += DumpMessage;
                b.DisconnectEvent += Disconnected;
                b.ReconnectOnPacketLoss(true);
                b.Connect();

                if (b.IsConnected() == true)
                {
                    isConnected = true;
                    mnuConnect.Enabled = false;
                    b.SendCommandPacket("");
                    keepAliveTimer.Start();

                    if (showStartupMsg == true)
                    {
                        b.SendCommandPacket(EBattlEyeCommand.Say, "-1 Whitelister started");
                    }
                }
            }
        }

        public void updateSettings()
        {
            ip = Properties.Settings.Default.rconIP;
            port = Properties.Settings.Default.rconPort;
            password = Properties.Settings.Default.rconPass;

            dbHost = Properties.Settings.Default.dbHost;
            dbPort = Properties.Settings.Default.dbPort.ToString();
            dbPassword = Properties.Settings.Default.dbPass;
            dbDatabase = Properties.Settings.Default.dbDatabase;
            dbUser = Properties.Settings.Default.dbUser;
        }

    }

    public class DayzClient
    {
        public string GUID { get; set; }
        public string IP { get; set; }
        public string UserName { get; set; }
        public string message { get; set; }
        public string email { get; set; }
        public int playerNo { get; set; }

        public LogTypes logType { get; set; }

        public enum LogTypes
        {
            Success = 1,
            Kick = 2
        }
    }

    public static class RichTextBoxExtensions
    {
        public static void AppendText(this RichTextBox box, string text, Color color)
        {
            box.SelectionStart = box.TextLength;
            box.SelectionLength = 0;

            box.SelectionColor = color;
            box.AppendText(" " + text + "\n");
            box.SelectionColor = box.ForeColor;
            box.SelectionStart = box.Text.Length;
            box.ScrollToCaret();
        }
    }

}
