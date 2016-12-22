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
        const int timeout = 5; //seconds 
        const string ReportServer = "TESTSESOSQL.ddc.local";
        const string ReportDB = "JENKINS";
        const string fqdn = ".ddc.local";

        private static void Main(string[] args)
        {
            var dt = getAllEnvironmentSecDB(ReportServer, ReportDB); //Retrieve the list of security databases from the specified server -- DT -> (SESecurityDB, EnvironmentID, SEDbServer)
            var allEndpoints = new List<endpointData>();
            foreach (DataRow row in dt.Rows) //iterates through the security databases
            {

                if (!string.IsNullOrEmpty(row["SESecurityDB"].ToString()))
                {
                    var ed = getEndpoints(row["SESecurityDB"].ToString(), (row["SEDbServer"].ToString()+fqdn), row["EnvironmentID"].ToString()); //pulls endpoints from a specified environment
                    allEndpoints.Add(ed); 
                }
            }
            foreach (var data in allEndpoints)
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
        static bool checkTcpPort(int port, String address, int timeout)
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
        static DataTable getAllEnvironmentSecDB(string SQLServer, string EnviromnentsDB)
        {
            string ConnectionString = $"Data Source={SQLServer};Initial Catalog={EnviromnentsDB};Integrated Security=SSPI;";
            SqlConnection sqlConn1 = new SqlConnection(ConnectionString);
            DataTable dt = new DataTable();


            string cmdtxt = $"SELECT SESecurityDB, EnvironmentID, SEDbServer  FROM [{EnviromnentsDB}].[dbo].[ENVIRONMENTS]";
            SqlCommand cmd = new SqlCommand(cmdtxt, sqlConn1);
            sqlConn1.Open();

            using (var adapter = new SqlDataAdapter(cmd))
            {
                adapter.Fill(dt);
            }

            sqlConn1.Close();
            if (dt.Rows.Count == 0)
            {
                Console.Write("Empty DataSet");
                throw (new Exception("No Data Returned"));
            }
            return dt;

        }
        static endpointData getEndpoints(string env, string SQLServer, string EnvID) 
        {
            string ConnectionString = $"Data Source={SQLServer};Initial Catalog={env};Integrated Security=SSPI;";
            SqlConnection sqlConn1 = new SqlConnection(ConnectionString);
            DataTable dt = new DataTable();
            DataTable dt2 = new DataTable();


            string cmdtxt = $"SELECT TOP 1 Address FROM[{env}].[dbo].[SEC11_EnvironmentEndpoints] WHERE Address like '%net.tcp%' and DatabaseId <> 'DEV_LOCAL'";
            using (var cmd = new SqlCommand(cmdtxt, sqlConn1))
            {
                try
                {
                    sqlConn1.Open();
                    using (var adapter = new SqlDataAdapter(cmd))
                    {
                        adapter.Fill(dt);
                    }
                }
                catch (Exception e)
                {
                    if (e is SqlException)
                    {
                        log("Unable to connect to DB: " + env + "  @" + SQLServer);
                    }
                    else log(e.ToString());
                    return null;
                    //throw e;
                }
            }
            string cmdtxt2 = $"SELECT TOP 1 Address FROM[{env}].[dbo].[SEC11_EnvironmentEndpoints] WHERE Address like '%http%' and DatabaseId <> 'DEV_LOCAL'";
            using (var cmd2 = new SqlCommand(cmdtxt2, sqlConn1))
            {
                try
                {
                    using (var adapter2 = new SqlDataAdapter(cmd2))
                    {
                        adapter2.Fill(dt2);
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
            ed.enviromnent = EnvID;
            ed.tcpEndpoint = dt.Rows[0]["Address"].ToString();
            ed.httpEndpoint = dt2.Rows[0]["Address"].ToString();
            return ed;
        }
        static void log(string s)
        {
            Console.WriteLine(s);
        }
        //static string printDataTable(DataTable dt)
        //{
        //    string s = null;
        //    foreach (DataRow dataRow in dt.Rows)
        //    {
        //        foreach (var item in dataRow.ItemArray)
        //        {
        //            s += item;
        //        }
        //    }
        //    return s;
        //}
        static bool writeSqlResults(endpointData toWrite)
        {
            string ConnectionString = $"Data Source={ReportServer};Initial Catalog={ReportDB};Integrated Security=SSPI;";
            var sqlConn1 = new SqlConnection(ConnectionString);
            sqlConn1.Open();

            string cmdtxt = "IF NOT EXISTS (SELECT 0 FROM dbo.SERVICE_HEALTH WHERE [EnvironmentID] = @environment) " +
                            "BEGIN " +
                            "INSERT INTO dbo.SERVICE_HEALTH([EnvironmentID],[TCPCheck], [LastChecked]) VALUES(@environment, @TCPHealth, @QTime) " +
                            "END " +
                            "ELSE " +
                            "UPDATE dbo.SERVICE_HEALTH SET [TCPCheck] = @TCPHealth, [LastChecked] = @QTime WHERE [EnvironmentId] = @environment";
            var cmd = new SqlCommand(cmdtxt, sqlConn1);
            var p1 = new SqlParameter();
            p1.ParameterName = "@environment";
            p1.Value = toWrite.enviromnent;
            cmd.Parameters.Add(p1);
            var p2 = new SqlParameter();
            p2.ParameterName = "@TCPHealth";
            if (toWrite.tcpHealth.ToString().Equals("False")){
                p2.Value = 0;
            }
            else
                p2.Value = 1;
            cmd.Parameters.Add(p2);
            var p3 = new SqlParameter();
            p3.ParameterName = "@QTime";
            p3.Value = toWrite.polltime;
            cmd.Parameters.Add(p3);
            var reader = cmd.ExecuteReader();
            reader.Close();
            sqlConn1.Close();
            return true;

        }
        class endpointData
        {
            public string enviromnent;
            public string tcpEndpoint;
            public string httpEndpoint;
            public int tcpPort;
            public string serv;
            public int httpPort;
            public bool tcpHealth;
            public bool httpHealth = false;
            public DateTime polltime;

            public endpointData()
            {
                polltime = DateTime.Now;
            }
            //public endpointData(string a, string b, string c)
            //{
            //    enviromnent = a;
            //    tcpEndpoint = b;
            //    httpEndpoint = c;
            //}
            public void extractInfo()
            {
                const string pattern = @":\/\/(.+):(\d+)\/";
                var rgx = new Regex(pattern, RegexOptions.IgnoreCase);
                var matches = rgx.Matches(tcpEndpoint);
                serv = matches[0].Groups[1].Value;
                tcpPort = Convert.ToInt32(matches[0].Groups[2].Value);
                var matches2 = rgx.Matches(httpEndpoint);
                httpPort = Convert.ToInt32(matches2[0].Groups[2].Value);
                polltime = DateTime.Now;
            }

        }
    }
}
