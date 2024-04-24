using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Threading.Tasks;

namespace jsemDebil
{
    internal class CzechPricesLocalisator
    {
        public void Run(string input)
        {
            DateTime userDate;
            DateTime currencyExchangeDate;
            if (input.ToString().Length != 0)
            {
                string USDRate;
                if (TryParseUserDate(input, out userDate, out currencyExchangeDate))
                {
                    Console.WriteLine($"The date was succesfully selected, getting data for {userDate.ToString(CultureInfo.CurrentCulture)}");
                    USDRate = GetCurrentUSDRate(currencyExchangeDate);
                }
                else
                {
                    Console.WriteLine($"The app was not able to parse the date you inputed, it used the current date instead: {DateTime.Today}");
                    USDRate = GetCurrentUSDRate(DateTime.Today);
                }
                if (float.TryParse(USDRate, out float floatUSDRate))
                {
                    EstablishConnectionToDB(floatUSDRate, currencyExchangeDate);
                }
                else  
                {
                    Console.WriteLine("NumberFormatException in Https response - Not users fault");
                }
            }
            else
            {
                Console.WriteLine("No arguments");
            }
        }
        static bool TryParseUserDate(string input, out DateTime userDate, out DateTime currencyExchangeDate)
        {
            if (DateTime.TryParse(input.Trim(), out userDate))
            {
                if (0 >= userDate.CompareTo(DateTime.Today))
                {
                    if (userDate.DayOfWeek == DayOfWeek.Saturday)
                    {
                        currencyExchangeDate = userDate.AddDays(-1);
                        return true;
                    }
                    else if (userDate.DayOfWeek == DayOfWeek.Sunday)
                    {
                        currencyExchangeDate = userDate.AddDays(-2);
                        return true;
                    }
                    else
                    {
                        currencyExchangeDate = userDate;
                        return true;
                    }
                }
            }
            userDate = DateTime.Now;
            currencyExchangeDate = DateTime.Now;
            return false;
        }
        static string GetData()
        {
            using (HttpClient client = new HttpClient())
            {
                try
                {
                    var url = "https://www.cnb.cz/cs/financni-trhy/devizovy-trh/kurzy-devizoveho-trhu/kurzy-devizoveho-trhu/rok.txt?rok=2024";
                    HttpResponseMessage response = client.GetAsync(url).Result; 
                    if (response.IsSuccessStatusCode)
                    {
                        string data = response.Content.ReadAsStringAsync().Result;
                        return data;
                    }
                    else
                    {
                        throw new Exception("Failed to retrieve ČNB rate data:"  + response.StatusCode);
                    }
                }
                catch (Exception ex)
                {
                    throw (new Exception(ex.Message));
                }
            }
        }
        static string GetCurrentUSDRate(DateTime currencyExchangeDate)
        {
            var data = GetData().Split('\n');
            var newestRates = data[data.Length - 2].Split('|');
            if (DateTime.TryParse(newestRates[0], new CultureInfo("cs-CZ"), DateTimeStyles.None, out DateTime newestDateWithRates) && newestDateWithRates > currencyExchangeDate) 
            {
                for (var i = 75; i < data.Length; i++)
                {
                    var ratesForOneDate = data[i].Split('|');
                    if (DateTime.TryParse(ratesForOneDate[0], new CultureInfo("cs-CZ"), DateTimeStyles.None, out DateTime result))
                    {
                        if (result > currencyExchangeDate)        
                        {
                            if (result == currencyExchangeDate)
                            {
                                Console.WriteLine($"CZK to USD rate for date {currencyExchangeDate} was found it is {ratesForOneDate[29]}");
                                return ratesForOneDate[29].Replace(',','.');
                            }
                        }
                    }
                    else
                    {
                        throw new Exception("Date from ČNB was in an unexpected format");
                    }
                }
                throw new Exception("Date was not found, it is out of range - This program can only access 2024 data for now...");
            }
            else
            {
                Console.WriteLine($"CZK to USD rate for the date {currencyExchangeDate} was not found, USD rate for the date {newestDateWithRates} was used instead  is {newestRates[29]}");
                return newestRates[29].Replace(',', '.');
            }
        }
        static void EstablishConnectionToDB(float USDRate, DateTime date)
        {
            try
            {
                var builder = new SqlConnectionStringBuilder();
                builder.DataSource = "stbechyn-sql.database.windows.net";
                builder.UserID = "prvniit";
                builder.Password = "P@ssW0rd!";
                builder.InitialCatalog = "AdventureWorksDW2020";

                using (SqlConnection connection = new SqlConnection(builder.ConnectionString))
                {
                    var sql = "select \"EnglishProductName\", round(\"StandardCost\", 0) as DealerPriceUSD, round(\"StandardCost\" * @USDRate, 0) as DealerPriceCZK from DimProduct order by \"EnglishProductName\"";
                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("@USDRate", USDRate);
                        connection.Open();
                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            SaveCZLocalisedData(reader, USDRate, date);
                        }
                    }
                }
            }
            catch (SqlException e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        static void SaveCZLocalisedData(SqlDataReader reader, float USDRate, DateTime date)
        {
            using (StreamWriter writetext = new StreamWriter($"./yyyymmdd-adventureworks.csv"))
            {
                writetext.WriteLine("{0} {1} {2} {3} {4}", "EnglishProductName", "DealerPriceUSD", "DealerPriceCZK", "Date", "RateForThatDate");
                while (reader.Read())
                {
                    if (!reader.IsDBNull(1))
                    {
                        writetext.WriteLine("{0} {1} {2} {3} {4}", reader.GetString(0), reader.GetDecimal(1), reader.GetDouble(2), date, USDRate);
                        //reader.GetDouble() is used instead of GetDecimal() because round() in the SQL query does not convert the type. So reader.GetDouble(2)  
                        //is still of type float so GetDecimal() can't be used because GetDecimal() does not allow for decimal numerals
                    }
                }
                Console.WriteLine("Query result successfully printed! Check your Debug folder.");
                Console.WriteLine("Rows with null values in StandardCost column were ignored.");
            }
        }
    }
}
