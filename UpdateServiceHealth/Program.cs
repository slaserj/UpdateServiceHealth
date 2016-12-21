using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace UpdateServiceHealth
{
    class Program
    {
        static int timeout = 5; //seconds 
        static string ReportServer = "TESTSESOSQL";
        static string ReportDB = "JENKINS";



        static void Main(string[] args)
        {
            DataTable dt = getAllEnvironmentSecDB("TESTSESOSQL", "Jenkins");
            List<endpointData> allEndpoints = new List<endpointData>();
            foreach (DataRow row in dt.Rows)
            {
                string s = row["SESecurityDB"].ToString();
                if (s != null)
                {
                    endpointData ed = getEndpoints(s, "TESTSESOSQL");
                    allEndpoints.Add(ed);
                }
            }
            foreach (endpointData data in allEndpoints)
            {

                if (data != null)
                {
                    data.extractInfo();
                    data.tcpHealth = checkTcpPort(data.tcpPort, data.serv, timeout);
                    log(data.enviromnent + " " + data.tcpEndpoint + " -- Health: " + data.tcpHealth);
                    writeSqlResults(data);
                }
                else log("No SQL information here, earlier connection errored");
                
            }
            Console.ReadKey();
        }
        static Boolean checkTcpPort(int port, String address, int timeout)
        {
            bool isAvailable = false;
            using (var client = new TcpClient())
            {
                try
                {
                    client.ReceiveTimeout = timeout * 1000;
                    client.SendTimeout = timeout * 1000;
                    var asyncResult = client.BeginConnect(address, port, null, null);
                    var waitHandle = asyncResult.AsyncWaitHandle;
                    try
                    {
                        if (!asyncResult.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(timeout), false))
                        {
                            client.Close();
                        }
                        else isAvailable = client.Connected;
                        client.EndConnect(asyncResult);
                    }
                    finally
                    {
                        waitHandle.Close();
                    }

                }
                catch { }
            }
            return isAvailable;
        }
        static DataTable getAllEnvironmentSecDB(String SQLServer, String EnviromnentsDB)
        {
            string ConnectionString = "Data Source=" + SQLServer + ";" + "Initial Catalog=" + EnviromnentsDB + ";" + "Integrated Security=SSPI;";
            SqlConnection sqlConn1 = new SqlConnection(ConnectionString);
            DataTable dt = new DataTable();


            string cmdtxt = "SELECT SESecurityDB FROM[" + EnviromnentsDB + "].[dbo].[ENVIRONMENTS]";
            SqlCommand cmd = new SqlCommand(cmdtxt, sqlConn1);
            sqlConn1.Open();

            using (SqlDataAdapter adapter = new SqlDataAdapter(cmd))
            {
                int rows_returned = adapter.Fill(dt);
            }

            sqlConn1.Close();
            if (dt.Rows.Count == 0)
            {
                Console.Write("Empty DataSet");
                throw (new Exception("No Data Returned"));
            }
            return dt;

        }
        static endpointData getEndpoints(String env, String SQLServer) 
        {
            string ConnectionString = "Data Source=" + SQLServer + ";" + "Initial Catalog=" + env + ";" + "Integrated Security=SSPI;";
            SqlConnection sqlConn1 = new SqlConnection(ConnectionString);
            DataTable dt = new DataTable();
            DataTable dt2 = new DataTable();


            string cmdtxt = "SELECT TOP 1 Address FROM[" + env + "].[dbo].[SEC11_EnvironmentEndpoints] WHERE Address like '%net.tcp%' and DatabaseId <> 'DEV_LOCAL'";
            using (SqlCommand cmd = new SqlCommand(cmdtxt, sqlConn1))
            {
                try
                {
                    sqlConn1.Open();
                    using (SqlDataAdapter adapter = new SqlDataAdapter(cmd))
                    {
                        int rows_returned = adapter.Fill(dt);
                    }
                }
                catch (Exception e)
                {
                    if (e is SqlException)
                    {
                        log("Unable to connect to DB: " + env);
                    }
                    else log(e.ToString());
                    return null;
                    //throw e;
                }
            }
            string cmdtxt2 = "SELECT TOP 1 Address FROM[" + env + "].[dbo].[SEC11_EnvironmentEndpoints] WHERE Address like '%http%' and DatabaseId <> 'DEV_LOCAL'";
            using (SqlCommand cmd2 = new SqlCommand(cmdtxt2, sqlConn1))
            {
                try
                {
                    using (SqlDataAdapter adapter2 = new SqlDataAdapter(cmd2))
                    {
                        int rows_returned2 = adapter2.Fill(dt2);
                    }
                }
                catch (Exception e)
                {
                    if (e is SqlException)
                    {
                        log("Unable to connect to DB: " + env);
                    }
                    else log(e.ToString());
                    return null;
                    //throw e;
                } 
               
                sqlConn1.Close();
            }
            if (dt.Rows.Count == 0)
            {
                log(new Exception("No Data Returned").ToString());
            }

            endpointData ed = new endpointData();
            //log(env + dt.Rows[0]["Address"].ToString() + dt2.Rows[0]["Address"].ToString());
            ed.enviromnent = env;
            ed.tcpEndpoint = dt.Rows[0]["Address"].ToString();
            ed.httpEndpoint = dt2.Rows[0]["Address"].ToString();
            return ed;
        }
        static void log(String s)
        {
            Console.WriteLine(s);
        }
        static String printDataTable(DataTable dt)
        {
            String s = null;
            foreach (DataRow dataRow in dt.Rows)
            {
                foreach (var item in dataRow.ItemArray)
                {
                    s += item;
                }
            }
            return s;
        }
        static Boolean writeSqlResults(endpointData toWrite)
        {
            string ConnectionString = "Data Source=" + ReportServer + ";" + "Initial Catalog=" + ReportDB + ";" + "Integrated Security=SSPI;";
            SqlConnection sqlConn1 = new SqlConnection(ConnectionString);
            sqlConn1.Open();
            string cmdtxt = "INSERT INTO dbo.SERVICE_HEALTH([EnvironmentName],[TCPCheck]) VALUES(@environment, @TCPHealth)";
            SqlCommand cmd = new SqlCommand(cmdtxt, sqlConn1);
            SqlParameter p1 = new SqlParameter();
            p1.ParameterName = "@environment";
            p1.Value = toWrite.enviromnent;
            cmd.Parameters.Add(p1);
            SqlParameter p2 = new SqlParameter();
            p2.ParameterName = "@TCPHealth";
            p2.Value = toWrite.tcpHealth.ToString();
            cmd.Parameters.Add(p2);
            SqlDataReader reader = cmd.ExecuteReader();
            if( reader != null)
            {
                reader.Close();
            }
            if (sqlConn1 != null)
            {
                sqlConn1.Close();
            }
            return true;

        }
        class endpointData
        {
            public string enviromnent = null;
            public string tcpEndpoint = null;
            public string httpEndpoint = null;
            public int tcpPort = 0;
            public string serv = null;
            public int httpPort = 0;
            public Boolean tcpHealth = false;
            public Boolean httpHealth = false;
            public DateTime polltime;

            public endpointData()
            {
                polltime = DateTime.Now;
            }
            public endpointData(string a, string b, string c)
            {
                enviromnent = a;
                tcpEndpoint = b;
                httpEndpoint = c;
            }
            public void extractInfo()
            {
                string pattern = @":\/\/(.+):(\d+)\/";
                Regex rgx = new Regex(pattern, RegexOptions.IgnoreCase);
                MatchCollection matches = rgx.Matches(tcpEndpoint);
                serv = matches[0].Groups[1].Value;
                tcpPort = Convert.ToInt32(matches[0].Groups[2].Value);
                MatchCollection matches2 = rgx.Matches(httpEndpoint);
                httpPort = Convert.ToInt32(matches2[0].Groups[2].Value);
                polltime = DateTime.Now;
            }

        }
    }
}
