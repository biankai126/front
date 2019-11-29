using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Net;
using System.Text;

namespace Slzhly.BaseApi.Controllers
{
    [ApiController]
    [Route(Program.AppName + "/[controller]")]
    public class HealthController : ControllerBase
    {
        private readonly IConfiguration _configurationRoot;

        /// <summary>
        /// Initializes a new instance of the <see cref="HealthController"/> class.
        /// </summary>
        public HealthController(IConfiguration configuration)
        {
            _configurationRoot = configuration;
        }
        /// <summary>
        /// 检查服务状态
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [AllowAnonymous]
        public IActionResult Get() => Ok($"{Program.IP}:{Program.Port} ok");

        /// <summary>
        /// 配置信息
        /// </summary>
        [HttpGet("config/{key}")]
        [AllowAnonymous]
        [ApiExplorerSettings(IgnoreApi = true)]
        public IActionResult GetValueForKey(string key)
        {
            return Ok(_configurationRoot[key]);
        }
        [HttpGet]
        [Route("GetEndHealth")]
        public IActionResult GetEndHealth()
        {
            var url = Environment.GetEnvironmentVariable("backservice");
            var reStr = HttpGet(url);
            return Ok(reStr);
        }
        public string HttpGet(string Url)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(Url);
            request.Method = "GET";
            HttpWebResponse response = (HttpWebResponse)request.GetResponse();
            Stream myResponseStream = response.GetResponseStream();
            StreamReader myStreamReader = new StreamReader(myResponseStream, Encoding.GetEncoding("utf-8"));
            string retString = myStreamReader.ReadToEnd();
            myStreamReader.Close();
            myResponseStream.Close();
            return retString;
        }
    }
}
