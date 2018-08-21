using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Web;
using Devart.Data.Oracle;
using Microsoft.Extensions.Configuration;
using RestSharp.Portable;
using RestSharp.Portable.Authenticators;
using RestSharp.Portable.HttpClient;
using Serilog;
using Wow.Notification.Api.Shared.Model;

namespace Wow.Mailers.Unicorn.Implementation
{
    public class MessageProcessor
    {
        public Dictionary<string, string> ErrorData { get; set; } = new Dictionary<string, string>();
        public IConfigurationRoot Configuration { get; }


        /// <summary>
        /// Initializes a new instance of the <see cref="MessageProcessor"/> class.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        public MessageProcessor(IConfigurationRoot configuration)
        {
            Configuration = configuration;
        }

        /// <summary>
        /// Processes the messages.
        /// </summary>
        public void ProcessMessages()
        {
            var startTime = DateTime.Now.ToString(CultureInfo.InvariantCulture);
            string emailTo = "noemail@wowinc.com";
            string toAddress = emailTo;

            try
            {
                var builder = new OracleConnectionStringBuilder();
                builder.Direct = true;
                builder.Server = Configuration["Oracle:Server"];
                builder.Sid = Configuration["Oracle:Sid"];
                builder.UserId = Configuration["Oracle:UserId"];
                builder.Password = Configuration["Oracle:Password"];
                builder.LicenseKey = Configuration["Oracle:LicenseKey"]; //@"trial:C:/Tools/Devart.Data.Oracle.key";

                OracleConnection connection = new OracleConnection(builder.ConnectionString);
                connection.Open();

                var maxSerialNo = GetMaxSerialNo(connection);
                string query = "select /*+ index(acc, pk_account) */ mast.EMAILSUBJECT,det.SERIALNO, det.TO_LIST, det.CC_LIST, det.BCC_LIST, det.TEMPLATECODE,dbms_lob.substr(det.BODY,32000,1) as MSGBODY, det.MSGSOURCE, det.STATUS, det.ACCOUNTNO , addr.corpid, mast.SENDERSEMAILID, mast.TEMPLATEID from uniprod.intf_emailmaster mast join uniprod.intf_emaildetails det on mast.TEMPLATECODE = det.templatecode left outer join uniprod.cc_account acc on det.accountno = acc.accountno left outer join uniprod.sa_addressmaster addr on acc.address3 = addr.addressid where  det.STATUS = 2"; //and det.SERIALNO < "
                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                      //+ maxSerialNo
                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                      //+ " order by det.serialno desc";

                OracleCommand command = connection.CreateCommand();
                command.CommandType = CommandType.Text;
                command.CommandText = query;

                using (OracleDataReader reader = command.ExecuteReader())
                {
                    if (!reader.HasRows)
                    {
                        Log.Information("No Records found for this date");

                        ErrorData.Clear();
                        ErrorData = BuildErrorParams("startEmail", "", "Empty Records", "0", "0", "0", "0", "", "");
                        var errorWriteResult = WriteErrorToDb(connection, ErrorData);

                        if (!errorWriteResult.status)
                        {
                            Log.Error("Error logging to Database failed");
                        }

                        ErrorData.Clear();
                        return; //nothing to process
                    }

                    while (reader.Read())
                    {
                        try
                        {
                            string useTestRecipient = Configuration["AppSettings:UseTestRecipient"];
                            if (useTestRecipient == "true")
                                toAddress = Configuration["AppSettings:TestRecipient"];
                            else
                                toAddress = reader["TO_LIST"].ToString();
                            string subject = reader["EMAILSUBJECT"].ToString();
                            string accountNumber = reader["ACCOUNTNO"].ToString();
                            string corpId = reader["CORPID"].ToString();
                            //string url = reader[""].ToString(); 

                            string msgBody = reader["MSGBODY"].ToString();
                            //msgBody = msgBody.Replace("#EBILLLINK#", url);
                            msgBody = msgBody.Replace("\n", "<br>");

                            string encryptedEmail = Encrypt3DES(connection, "em=" + toAddress + "&ac=" + accountNumber);
                            encryptedEmail = HttpUtility.UrlEncode(encryptedEmail);

                            //create an object to send
                            var emailNotification = new EmailNotificationRequest
                            {
                                EmailTo = toAddress,
                                ExternalReferenceId = $"Ebill-{accountNumber}",
                                IsTemplateBased = false,
                                TemplateId = 0,
                                Title = subject,
                                NotificationBody = msgBody,

                                TemplateFields = new Dictionary<string, string>
                                {
                                    { "Email", encryptedEmail },
                                    { "NumberOfDays", Configuration["AppSettings:NOOFDAYS"]}
                                }

                            };
                            var notificationApiConsumer = new NotificationApiConsumer(Configuration);
                            bool emailResponse = notificationApiConsumer.SendEmailNotification(emailNotification)
                                .taskStatus;

                            if (!emailResponse)
                            {
                                UpdateRecord(connection, Convert.ToInt64(reader["SERIALNO"].ToString()),
                                    Convert.ToInt32(reader["ACCOUNTNO"].ToString()), 2);

                                ErrorData.Clear();
                                ErrorData = BuildErrorParams("startEmail", "", "response.Content", accountNumber, "0",
                                    corpId, "0", toAddress, startTime);
                                var errorWriteResult = WriteErrorToDb(connection, ErrorData);

                                if (!errorWriteResult.status)
                                {
                                    Log.Error("Error logging to Database failed");
                                }

                                ErrorData.Clear();
                                Log.Error("Error EMail Sent Serial Number: " + maxSerialNo + " Account Number: " +
                                          accountNumber + " corpID : " + corpId + " EmailAddress: " + toAddress);
                            }
                            else
                            {
                                UpdateRecord(connection, Convert.ToInt64(reader["SERIALNO"].ToString()),
                                    Convert.ToInt32(reader["ACCOUNTNO"].ToString()), 1);
                                Log.Information("EMail Sent Serial Number: " + maxSerialNo + " Account Number: " +
                                                accountNumber + " corpID : " + corpId + " EmailAddress: " + toAddress);
                            }
                        }
                        catch (Exception ex)
                        {
                            ErrorData.Clear();
                            ErrorData = BuildErrorParams("startEmail", "", ex.Message, "0", "0", "0", "0", "",
                                startTime);
                            var errorWriteResult = WriteErrorToDb(connection, ErrorData);

                            if (!errorWriteResult.status)
                            {
                                Log.Error("Error logging to Database failed");
                            }

                            ErrorData.Clear();  //let it continue, as there might be other messages to be sent out
                        }
                    }
                }

                connection.Close();
            }
            catch (Exception e)
            {
                Log.Error(e, "Error while processing the messages");
            }
        }

        /// <summary>
        /// Updates the record.
        /// </summary>
        /// <param name="connection">The connection.</param>
        /// <param name="serialNumber">The serial no.</param>
        /// <param name="accountNumber">The account number.</param>
        /// <param name="status">The status.</param>
        private void UpdateRecord(OracleConnection connection, long serialNumber, int accountNumber, int status)
        {
            String starttime = DateTime.Now.ToString(CultureInfo.InvariantCulture);
            try
            {
                OracleCommand command = connection.CreateCommand();
                command.CommandType = CommandType.StoredProcedure;
                command.CommandText = "uniprod.email_pack.sp_updateemailrecord"; //not sure if this still the case
                command.Parameters.Add(new OracleParameter("inserialno", OracleDbType.Integer)).Value = serialNumber;
                command.Parameters.Add(new OracleParameter("inaccountno", OracleDbType.Integer)).Value = accountNumber;
                command.Parameters.Add(new OracleParameter("instatus", OracleDbType.Integer)).Value = status;
                command.Parameters.Add(new OracleParameter("inerrormsg", OracleDbType.VarChar)).Value = "Email Sent";
                command.Parameters.Add(new OracleParameter("inemailsenton", OracleDbType.Date)).Value = DateTime.Now;
                command.Parameters.Add(new OracleParameter("outreturnmsg", OracleDbType.VarChar, 255)).Direction = ParameterDirection.Output;

                command.ExecuteReader();
                string resultOut = command.Parameters["outreturnmsg"].Value.ToString();
            }
            catch (Exception ex)
            {
                ErrorData.Clear();
                ErrorData = BuildErrorParams("updateRecord", "SerialNo : " + serialNumber + "- Account No : " + accountNumber + " - status : " + status, ex.Message, "0", "0", "0", "0", "", starttime);
                var errorWriteResult = WriteErrorToDb(connection, ErrorData);

                if (!errorWriteResult.status)
                {
                    Log.Error("Error logging to database failed.");
                }

                ErrorData.Clear();
            }

        }

        /// <summary>
        /// Encrypt3s the DES.
        /// </summary>
        /// <param name="connection">The connection.</param>
        /// <param name="valueToEncrypt">The value to encrypt.</param>
        /// <returns></returns>
        private string Encrypt3DES(OracleConnection connection, string valueToEncrypt)
        {
            String starttime = DateTime.Now.ToString(CultureInfo.InvariantCulture);
            try
            {
                string Key = "4E8DBD21";
                DESCryptoServiceProvider desCryptoServiceProvider = new DESCryptoServiceProvider();

                desCryptoServiceProvider.Key = System.Text.Encoding.UTF8.GetBytes(Key);
                desCryptoServiceProvider.Mode = CipherMode.ECB;
                desCryptoServiceProvider.Padding = PaddingMode.Zeros;

                ICryptoTransform desEncrypt = desCryptoServiceProvider.CreateEncryptor();
                byte[] inputBuffer = System.Text.Encoding.UTF8.GetBytes(valueToEncrypt);

                return Convert.ToBase64String(desEncrypt.TransformFinalBlock(inputBuffer, 0, inputBuffer.Length));
            }
            catch (Exception ex)
            {
                ErrorData.Clear();
                ErrorData = BuildErrorParams("Encrypt3DES", "strString : " + valueToEncrypt, ex.Message, "0", "0", "0", "0", "", starttime);
                var errorWriteResult = WriteErrorToDb(connection, ErrorData);

                if (!errorWriteResult.status)
                {
                    Log.Error("Error logging to database failed.");
                }

                ErrorData.Clear();
                return "";
            }
        }


        /// <summary>
        /// Gets the maximum serial no.
        /// </summary>
        /// <param name="connection">The connection.</param>
        /// <returns></returns>
        private string GetMaxSerialNo(OracleConnection connection)
        {

            string strResult = string.Empty;
            String starttime = DateTime.Now.ToString(CultureInfo.InvariantCulture);
            try
            {
                string query = "select max(serialno) as MAXNO from uniprod.intf_emaildetails";
                OracleCommand command = connection.CreateCommand();
                command.CommandType = CommandType.Text;
                command.CommandText = query;

                strResult = command.ExecuteScalar().ToString();
            }
            catch (Exception ex)
            {
                ErrorData.Clear();
                ErrorData = BuildErrorParams("getMaxSerialNo", "", ex.Message, "0", "0", "0", "0", "", starttime);
                var errorWriteResult = WriteErrorToDb(connection, ErrorData);

                if (!errorWriteResult.status)
                {
                    Log.Error("Error logging to database failed.");
                }

                ErrorData.Clear();

            }
            return strResult;
        }

        /// <summary>
        /// Builds the error parameters.
        /// </summary>
        /// <param name="method">The method.</param>
        /// <param name="parameters">The parameters.</param>
        /// <param name="result">The result.</param>
        /// <param name="accoutNo">The accout no.</param>
        /// <param name="billCycle">The bill cycle.</param>
        /// <param name="billGroup">The bill group.</param>
        /// <param name="addressid">The addressid.</param>
        /// <param name="email">The email.</param>
        /// <param name="startTime">The start time.</param>
        /// <returns></returns>
        private static Dictionary<string, string> BuildErrorParams(string method, string parameters, string result, string accoutNo, string billCycle, string billGroup, string addressid, string email, string startTime)
        {
            Dictionary<string, string> dtParams = new Dictionary<string, string>();

            try
            {
                dtParams.Add("Method", method);
                dtParams.Add("Parameters", parameters);
                dtParams.Add("Result", result);
                dtParams.Add("AccountNo", accoutNo);
                dtParams.Add("billCycle", billCycle);
                dtParams.Add("billGroup", billGroup);
                dtParams.Add("AddressId", addressid);
                dtParams.Add("Email", email);
                dtParams.Add("ServerName", Environment.MachineName);
                dtParams.Add("EndTime", DateTime.Now.ToString(CultureInfo.InvariantCulture));
                dtParams.Add("startTime", startTime);
                return dtParams;
            }
            catch (Exception ex)
            {
                Log.Error("Error in building error parameters : " + ex.Message);
                return dtParams;
            }
        }

        /// <summary>
        /// Writes the error to database.
        /// </summary>
        /// <param name="connection">The connection.</param>
        /// <param name="dataToWrite">The data to write.</param>
        /// <returns></returns>
        public (bool status, string message) WriteErrorToDb(OracleConnection connection, Dictionary<string, string> dataToWrite)
        {
            Log.Error("Error in Unicorn Mailer" + "||" + dataToWrite["Method"] + "||" +
                      dataToWrite["Parameters"] + "||" + dataToWrite["Result"] + "||" +
                      dataToWrite["AccountNo"] + "||" + dataToWrite["BillGroup"] + "||" + dataToWrite["BillCycle"] +
                       "||" + dataToWrite["AddressId"] + "||" + dataToWrite["Email"] + "||" + dataToWrite["ServerName"] +
                       "||" + dataToWrite["startTime"] + "||" + dataToWrite["EndTime"]);

            return (true, "");
        }
    }
}
