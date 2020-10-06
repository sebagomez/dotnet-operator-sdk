using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using k8s;
using k8s.Models;
using ContainerSolutions.OperatorSDK;
using NLog;

namespace mssql_db
{
    public class MSSQLDBOperationHandler : IOperationHandler<MSSQLDB>
    {
        Dictionary<string, MSSQLDB> m_currentState = new Dictionary<string, MSSQLDB>();

        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        SqlConnection GetDBConnection(Kubernetes k8s, MSSQLDB db)
        {
            var configMap = k8s.ReadNamespacedConfigMap(db.Spec.ConfigMap, db.Namespace());
            string instance = configMap.Data["instance"];
            var secret = k8s.ReadNamespacedSecret(db.Spec.Credentials, db.Namespace());
            string dbUser = ASCIIEncoding.UTF8.GetString(secret.Data["userid"]);
            string password = ASCIIEncoding.UTF8.GetString(secret.Data["password"]);

            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder
            {
                DataSource = instance,
                UserID = dbUser,
                Password = password,
                InitialCatalog = "master"
            };

            return new SqlConnection(builder.ConnectionString);
        }

        public Task OnAdded(Kubernetes k8s, MSSQLDB crd)
        {
            lock (m_currentState)
                CreateDB(k8s, crd);

            return Task.CompletedTask;
        }

        public Task OnBookmarked(Kubernetes k8s, MSSQLDB crd)
        {
            Log.Warn($"DATABASE {crd.Spec.DBName} was BOOKMARKED (???)");

            return Task.CompletedTask;
        }

        public Task OnDeleted(Kubernetes k8s, MSSQLDB crd)
        {
            lock (m_currentState)
            {
                Log.Info($"DATABASE {crd.Spec.DBName} will be DELETED!");

                using (SqlConnection connection = GetDBConnection(k8s, crd))
                {
                    connection.Open();

                    try
                    {
                        SqlCommand createCommand = new SqlCommand($"DROP DATABASE {crd.Spec.DBName};", connection);
                        int i = createCommand.ExecuteNonQuery();
                    }
                    catch (SqlException sex)
                    {
                        if (sex.Number == 3701) //Already gone!
                        {
                            Log.Error(sex.Message);
                            return Task.CompletedTask;
                        }

                        Log.Error(sex.Message);
                        return Task.CompletedTask;
                    }
                    catch (Exception ex)
                    {
                        Log.Fatal(ex.Message);
                        return Task.CompletedTask;
                    }

                    m_currentState.Remove(crd.Name());
                    Log.Info($"DATABASE {crd.Spec.DBName} successfully deleted!");

                }

                return Task.CompletedTask;
            }
        }

        public Task OnError(Kubernetes k8s, MSSQLDB crd)
        {
            Log.Error($"ERROR on {crd.Spec.DBName}");

            return Task.CompletedTask;
        }

        public Task OnUpdated(Kubernetes k8s, MSSQLDB crd)
        {
            Log.Warn($"DATABASE {crd.Spec.DBName} was UPDATED");

            MSSQLDB currentDb = m_currentState[crd.Name()];

            if (currentDb.Spec.DBName != crd.Spec.DBName)
            {
                try
                {
                    RenameDB(k8s, currentDb, crd);
                    Log.Info($"Database sucessfully renamed from {currentDb.Spec.DBName} to {crd.Spec.DBName}");
                    m_currentState[crd.Name()] = crd;
                }
                catch (Exception ex)
                {
                    Log.Fatal(ex);
                }
            }
            else
                m_currentState[crd.Name()] = crd;

            return Task.CompletedTask;
        }

        public Task CheckCurrentState(Kubernetes k8s)
        {
            lock (m_currentState)
            {
                foreach (string key in m_currentState.Keys.ToList())
                {
                    MSSQLDB db = m_currentState[key];
                    using (SqlConnection connection = GetDBConnection(k8s, db))
                    {
                        connection.Open();
                        SqlCommand queryCommand = new SqlCommand($"SELECT COUNT(*) FROM SYS.DATABASES WHERE NAME = '{db.Spec.DBName}';", connection);

                        try
                        {
                            int i = (int)queryCommand.ExecuteScalar();

                            if (i == 0)
                            {
                                Log.Warn($"Database {db.Spec.DBName} was not found!");
                                CreateDB(k8s, db);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex.Message);
                        }
                    }
                }
            }

			return Task.CompletedTask;
        }

        void CreateDB(Kubernetes k8s, MSSQLDB db)
        {
            Log.Info($"DATABASE {db.Spec.DBName} will be ADDED");

            using (SqlConnection connection = GetDBConnection(k8s, db))
            {
                connection.Open();

                try
                {
                    SqlCommand createCommand = new SqlCommand($"CREATE DATABASE {db.Spec.DBName};", connection);
                    int i = createCommand.ExecuteNonQuery();
                }
                catch (SqlException sex)
                {
                    if (sex.Number == 1801) //Database already exists
                    {
                        Log.Warn(sex.Message);
                        m_currentState[db.Name()] = db;
                        return;
                    }

                    Log.Error(sex.Message);
                    return;
                }
                catch (Exception ex)
                {
                    Log.Fatal(ex.Message);
                    return;
                }

                m_currentState[db.Name()] = db;
                Log.Info($"DATABASE {db.Spec.DBName} successfully created!");
            }
        }

        void RenameDB(Kubernetes k8s, MSSQLDB currentDB, MSSQLDB newDB)
        {
            string sqlCommand = @$"ALTER DATABASE {currentDB.Spec.DBName} SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
ALTER DATABASE {currentDB.Spec.DBName} MODIFY NAME = {newDB.Spec.DBName};
ALTER DATABASE {newDB.Spec.DBName} SET MULTI_USER;";

            using (SqlConnection connection = GetDBConnection(k8s, newDB))
            {
                connection.Open();
                SqlCommand command = new SqlCommand(sqlCommand, connection);
                command.ExecuteNonQuery();
            }
        }
    }
}
