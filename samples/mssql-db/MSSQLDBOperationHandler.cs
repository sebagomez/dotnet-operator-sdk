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
using Microsoft.Rest;

namespace mssql_db
{
    public class MSSQLDBOperationHandler : IOperationHandler<MSSQLDB>
    {
        const string INSTANCE = "instance";
        const string USER_ID = "userid";
        const string PASSWORD = "password";
        const string MASTER = "master";

        Dictionary<string, MSSQLDB> m_currentState = new Dictionary<string, MSSQLDB>();

        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        SqlConnection GetDBConnection(Kubernetes k8s, MSSQLDB db)
        {
            var configMap = GetConfigMap(k8s, db);
            if (!configMap.Data.ContainsKey(INSTANCE))
                throw new ApplicationException($"ConfigMap '{configMap.Name()}' does not contain the '{INSTANCE}' data property.");

            string instance = configMap.Data[INSTANCE];
            
            var secret = GetSecret(k8s, db);
            if (!secret.Data.ContainsKey(USER_ID))
                throw new ApplicationException($"Secret '{secret.Name()}' does not contain the '{USER_ID}' data property.");

            if (!secret.Data.ContainsKey(PASSWORD))
                throw new ApplicationException($"Secret '{secret.Name()}' does not contain the '{PASSWORD}' data property.");

            string dbUser = ASCIIEncoding.UTF8.GetString(secret.Data[USER_ID]);
            string password = ASCIIEncoding.UTF8.GetString(secret.Data[PASSWORD]);

            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder
            {
                DataSource = instance,
                UserID = dbUser,
                Password = password,
                InitialCatalog = MASTER
            };

            return new SqlConnection(builder.ConnectionString);
        }

        V1ConfigMap GetConfigMap(Kubernetes k8s, MSSQLDB db)
        {
            try
            {
                return k8s.ReadNamespacedConfigMap(db.Spec.ConfigMap, db.Namespace());
            }
            catch (HttpOperationException hoex) when (hoex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                throw new ApplicationException($"ConfigMap '{db.Spec.ConfigMap}' not found in namespace {db.Namespace()}");
            }
        }

        V1Secret GetSecret(Kubernetes k8s, MSSQLDB db)
        {
            try
            {
                return k8s.ReadNamespacedSecret(db.Spec.Credentials, db.Namespace());
            }
            catch (HttpOperationException hoex) when (hoex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                throw new ApplicationException($"Secret '{db.Spec.Credentials}' not found in namespace {db.Namespace()}");
            }
        }

        public Task OnAdded(Kubernetes k8s, MSSQLDB crd)
        {
            lock (m_currentState)
                CreateDB(k8s, crd);

            return Task.CompletedTask;
        }

        public Task OnBookmarked(Kubernetes k8s, MSSQLDB crd)
        {
            Log.Warn($"MSSQLDB {crd.Name()} was BOOKMARKED (???)");

            return Task.CompletedTask;
        }

        public Task OnDeleted(Kubernetes k8s, MSSQLDB crd)
        {
            lock (m_currentState)
            {
                Log.Info($"MSSQLDB {crd.Name()} must be deleted! ({crd.Spec.DBName})");

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
                    Log.Info($"Database {crd.Spec.DBName} successfully dropped!");

                }

                return Task.CompletedTask;
            }
        }

        public Task OnError(Kubernetes k8s, MSSQLDB crd)
        {
            Log.Error($"ERROR on {crd.Name()}");

            return Task.CompletedTask;
        }

        public Task OnUpdated(Kubernetes k8s, MSSQLDB crd)
        {
            Log.Info($"MSSQLDB {crd.Name()} was updated. ({crd.Spec.DBName})");

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
                    throw;
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
                                Log.Warn($"Database {db.Spec.DBName} ({db.Name()}) was not found!");
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
            Log.Info($"Database {db.Spec.DBName} must be created.");

            using (SqlConnection connection = GetDBConnection(k8s, db))
            {
                connection.Open();

                try
                {
                    SqlCommand createCommand = new SqlCommand($"CREATE DATABASE {db.Spec.DBName};", connection);
                    int i = createCommand.ExecuteNonQuery();
                }
                catch (SqlException sex) when (sex.Number == 1801) //Database already exists
                {
                    Log.Warn(sex.Message);
                    m_currentState[db.Name()] = db;
                    return;
                }
                catch (Exception ex)
                {
                    Log.Fatal(ex.Message);
                    throw;
                }

                m_currentState[db.Name()] = db;
                Log.Info($"Database {db.Spec.DBName} successfully created!");
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
