using System;
using System.Text.RegularExpressions;
using System.Net.Cache;
using System.Net;
using System.IO;

namespace p2pcopy
{
    static class InternetTime
    {
        static internal DateTime Get()
        {
            // http://stackoverflow.com/questions/6435099/how-to-get-datetime-from-the-internet

            DateTime dateTime = DateTime.MinValue;

            System.Net.ServicePointManager.SecurityProtocol = 
                (SecurityProtocolType)(0xc0 | 0x300 | 0xc00);
                // SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create ("http://worldtimeapi.org/api/timezone/Europe/London.txt");
            request.Method = "GET";
            request.Accept = "text/html, application/xhtml+xml, */*";
            request.UserAgent = "p2pcopy";
            request.ContentType = "application/x-www-form-urlencoded";
            request.CachePolicy = new RequestCachePolicy(RequestCacheLevel.NoCacheNoStore); //No caching
            HttpWebResponse response = (HttpWebResponse)request.GetResponse();
            if (response.StatusCode == HttpStatusCode.OK)
            {
                StreamReader stream = new StreamReader(response.GetResponseStream());
                string html = stream.ReadToEnd();//<timestamp time=\"1395772696469995\" delay=\"1395772696469995\"/>                
                string time = Regex.Match(html, @"(?<=unixtime: )[^u]*").Value;
                double milliseconds = Convert.ToInt64(time) * 1000.0;
                dateTime = new DateTime(1970, 1, 1).AddMilliseconds(milliseconds).ToLocalTime();
            }

            return dateTime;
        }
    }
}
