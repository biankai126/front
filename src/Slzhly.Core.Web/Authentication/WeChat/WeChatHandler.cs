﻿using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;

namespace Slzhly.Core.Web.Authentication.WeChat
{
    internal class WeChatHandler : OAuthHandler<WeChatOptions>
    {
        private readonly ISecureDataFormat<AuthenticationProperties> _secureDataFormat;
        private readonly ILogger _logger;
        /// <summary>
        /// Called after options/events have been initialized for the handler to finish initializing itself.
        /// </summary>
        /// <returns>A task</returns>
        protected override async Task InitializeHandlerAsync()
        {
            await base.InitializeHandlerAsync();
            if (Options.UseCachedStateDataFormat)
            {
                Options.StateDataFormat = _secureDataFormat;
            }
        }

        public WeChatHandler(
             IOptionsMonitor<WeChatOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            ISystemClock clock,
            ISecureDataFormat<AuthenticationProperties> secureDataFormat)
            : base(options, logger, encoder, clock)
        {
            _secureDataFormat = secureDataFormat;
            _logger = logger.CreateLogger(nameof(WeChatHandler));
        }
        protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            if (string.IsNullOrEmpty(properties.RedirectUri))
            {
                properties.RedirectUri = OriginalPathBase + OriginalPath + Request.QueryString;
            }

            // OAuth2 10.12 CSRF
            GenerateCorrelationId(properties);

            var authorizationEndpoint = BuildChallengeUrl(properties, BuildRedirectUri(Options.CallbackPath));
            var redirectContext = new RedirectContext<OAuthOptions>(
                Context, Scheme, Options,
                properties, authorizationEndpoint);
            _logger.LogDebug($"RedirectUri=> {redirectContext.RedirectUri}");
            await Events.RedirectToAuthorizationEndpoint(redirectContext);

            var location = Context.Response.Headers[HeaderNames.Location];
            if (location == StringValues.Empty)
            {
                location = "(not set)";
            }
            var cookie = Context.Response.Headers[HeaderNames.SetCookie];
            if (cookie == StringValues.Empty)
            {
                cookie = "(not set)";
            }
            _logger.LogDebug($"WeChatHandler HandleChallenge with Location: {location}; and Set-Cookie: {cookie}.");
        }

        /*
         * Challenge 盘问握手认证协议
         * 这个词有点偏，好多翻译工具都查不出。
         * 这个解释才是有些靠谱 http://abbr.dict.cn/Challenge/CHAP
         */
        /// <summary>
        /// 构建请求CODE的Url地址（这是第一步，准备工作）
        /// </summary>
        /// <param name="properties"></param>
        /// <param name="redirectUri"></param>
        /// <returns></returns>
        protected override string BuildChallengeUrl(AuthenticationProperties properties, string redirectUri)
        {
            var scope = FormatScope();

            var state = Options.StateDataFormat.Protect(properties);

            if (!string.IsNullOrEmpty(Options.CallbackUrl))
            {
                redirectUri = Options.CallbackUrl;
            }

            var parameters = new Dictionary<string, string>()
            {
                { "appid", Options.ClientId },
                { "redirect_uri", redirectUri },
                { "response_type", "code" },
                //{ "scope", scope },
                //{ "state", state },
            };
            //判断当前请求是否由微信内置浏览器发出
            var isMicroMessenger = Options.IsWeChatBrowser(Request);
            var ret = QueryHelpers.AddQueryString(
                isMicroMessenger ? Options.AuthorizationEndpoint2
                    : Options.AuthorizationEndpoint, parameters);
            //scope 不能被UrlEncode
            ret += $"&scope={scope}&state={state}";
            _logger.LogDebug("请求CODE " + ret);
            return ret;
        }
        /// <summary>
        /// 处理微信授权结果（接收微信授权的回调）
        /// </summary>
        /// <returns></returns>
        protected override async Task<HandleRequestResult> HandleRemoteAuthenticateAsync()
        {
            //第一步，处理工作
            var query = Request.Query;

            //微信只会发送code和state两个参数，不会返回错误消息
            //若用户禁止授权，则重定向后不会带上code参数，仅会带上state参数
            var code = query["code"];
            var state = query["state"];
            _logger.LogDebug($"接收微信授权的回调 code:{code} state:{state}");
            var properties = Options.StateDataFormat.Unprotect(state);
            if (properties == null)
            {
                return HandleRequestResult.Fail("认证状态(state)丢失或无效.");
            }

            // OAuth2 10.12 CSRF
            if (!ValidateCorrelationId(properties))
            {
                _logger.LogWarning("Correlation failed.");
                //return HandleRequestResult.Fail("Correlation failed.");
            }

            if (StringValues.IsNullOrEmpty(code)) //code为null就是
            {
                return HandleRequestResult.Fail("Code was not found.", properties);
            }

            //第二步，通过Code获取Access Token
            var redirectUrl = !string.IsNullOrEmpty(Options.CallbackUrl) ?
                Options.CallbackUrl :
                BuildRedirectUri(Options.CallbackPath);
            var codeExchangeContext = new OAuthCodeExchangeContext(properties, code, redirectUrl);
            using var tokens = await ExchangeCodeAsync(codeExchangeContext);

            if (tokens.Error != null)
            {
                return HandleRequestResult.Fail(tokens.Error, properties);
            }

            if (string.IsNullOrEmpty(tokens.AccessToken))
            {
                return HandleRequestResult.Fail("获取 access token 失败.", properties);
            }

            var identity = new ClaimsIdentity(ClaimsIssuer);

            if (Options.SaveTokens)
            {
                var authTokens = new List<AuthenticationToken>();

                authTokens.Add(new AuthenticationToken { Name = "access_token", Value = tokens.AccessToken });
                if (!string.IsNullOrEmpty(tokens.RefreshToken))
                {
                    authTokens.Add(new AuthenticationToken { Name = "refresh_token", Value = tokens.RefreshToken });
                }

                if (!string.IsNullOrEmpty(tokens.TokenType)) //微信就没有这个
                {
                    authTokens.Add(new AuthenticationToken { Name = "token_type", Value = tokens.TokenType });
                }

                if (!string.IsNullOrEmpty(tokens.ExpiresIn))
                {
                    int value;
                    if (int.TryParse(tokens.ExpiresIn, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                    {
                        // https://www.w3.org/TR/xmlschema-2/#dateTime
                        // https://msdn.microsoft.com/en-us/library/az4se3k1(v=vs.110).aspx
                        var expiresAt = Clock.UtcNow + TimeSpan.FromSeconds(value);
                        authTokens.Add(new AuthenticationToken
                        {
                            Name = "expires_at",
                            Value = expiresAt.ToString("o", CultureInfo.InvariantCulture)
                        });
                    }
                }

                properties.StoreTokens(authTokens);
            }

            var ticket = await CreateTicketAsync(identity, properties, tokens);
            if (ticket != null)
            {
                return HandleRequestResult.Success(ticket);
            }
            else
            {
                return HandleRequestResult.Fail("无法从远程服务器获取用户信息", properties);
            }
        }

        /// <summary>
        /// 通过Code获取Access Token(这是第二步) 
        /// </summary>
        protected override async Task<OAuthTokenResponse> ExchangeCodeAsync(OAuthCodeExchangeContext context)
        {
            var parameters = new Dictionary<string, string>
            {
                {  "appid", Options.ClientId },
                {  "secret", Options.ClientSecret },
                {  "code", context.Code},
                {  "grant_type", "authorization_code" }
            };
            _logger.LogDebug("code换取access_token");
            var endpoint = QueryHelpers.AddQueryString(Options.TokenEndpoint, parameters);
            _logger.LogDebug(endpoint);
            var response = await Backchannel.GetAsync(endpoint, Context.RequestAborted);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadAsStringAsync();
                _logger.LogDebug(result);
                var payload = JsonDocument.Parse(result);
                if (payload.RootElement.TryGetProperty("errcode",out JsonElement errcode))
                {
                    return OAuthTokenResponse.Failed(new Exception($"获取微信AccessToken出错。{errcode}"));
                }
                return OAuthTokenResponse.Success(payload);
            }
            else
            {
                return OAuthTokenResponse.Failed(new Exception("获取微信AccessToken出错。"));
            }
        }

        /// <summary>
        /// 创建身份票据(这是第三步) 
        /// </summary>
        /// <param name="identity"></param>
        /// <param name="properties"></param>
        /// <param name="tokens"></param>
        /// <returns></returns>
        protected override async Task<AuthenticationTicket> CreateTicketAsync(ClaimsIdentity identity, AuthenticationProperties properties, OAuthTokenResponse tokens)
        {
            var openId = GetOpenId(tokens.Response);
            var unionId = GetUnionId(tokens.Response);
            var user = tokens.Response;
            //微信获取用户信息是需要开通权限的，没有开通权限的只能用openId来标示用户
            if (!string.IsNullOrEmpty(unionId))
            {
                //获取用户信息
                var parameters = new Dictionary<string, string>
                {
                    {  "openid", openId},
                    {  "access_token", tokens.AccessToken },
                    {  "lang", "zh-CN" } //如果是多语言，这个参数该怎么获取？
                };
                var userInfoEndpoint = QueryHelpers.AddQueryString(Options.UserInformationEndpoint, parameters);
                var userInfoResponse = await Backchannel.GetAsync(userInfoEndpoint, Context.RequestAborted);
                if (!userInfoResponse.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"未能获取到微信用户个人信息(返回状态码:{userInfoResponse.StatusCode})，请检查access_token是正确。");
                }

                user = JsonDocument.Parse(await userInfoResponse.Content.ReadAsStringAsync());
            }

            var context = new OAuthCreatingTicketContext(new ClaimsPrincipal(identity), properties, Context, Scheme, Options, Backchannel, tokens, user.RootElement);
            context.RunClaimActions();
            await Events.CreatingTicket(context);
            return new AuthenticationTicket(context.Principal, context.Properties, Scheme.Name);
        }

        private static string GetOpenId(JsonDocument json)
        {
            return json.RootElement.GetProperty("openid").GetString();
        }
        private static string GetUnionId(JsonDocument json)
        {
            return json.RootElement.GetProperty("unionid").GetString();
        }

        /// <summary>
        /// 根据是否为微信浏览器返回不同Scope
        /// </summary>
        /// <returns></returns>
        protected override string FormatScope()
        {
            if (Options.IsWeChatBrowser(Request))
            {
                return string.Join(",", Options.Scope2);
            }
            else
            {
                return string.Join(",", Options.Scope);
            }
        }
    }
}
