﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;


class Program
{
    static void Main()
    {
        const string clientID = "";
        const string clientSecret = "";

        // Generates code verifier value.
        string codeVerifier = RandomDataBase64Url(32);

        // Creates a redirect URI using an available port on the loopback address.
        string redirectURI = string.Format("http://{0}:{1}/", IPAddress.Loopback, GetRandomUnusedPort());
        Console.WriteLine("redirect URI: " + redirectURI);

        // Extracts the authorization code.
        var authorizationCode = GetAuthorizationCode(clientID, codeVerifier, redirectURI);

        // Obtains the access token from the authorization code.
        string responseText = GetAccessToken(authorizationCode, clientID, clientSecret, codeVerifier, redirectURI);
        Dictionary<string, string> tokenEndpointDecoded = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(responseText);

        string accessToken = tokenEndpointDecoded["access_token"];
        string str = tokenEndpointDecoded["refresh_token"];

        GetuserProfile(accessToken);

        Console.ReadKey();

        // Uses the access token to authenticate to a user's Gmail account

    }

    static string GetAuthorizationCode(string clientID, string codeVerifier, string redirectURI)
    {
        // Generates state and PKCE values.
        string state = RandomDataBase64Url(32);
        string codeChallenge = Base64UrlEncodeNoPadding(Sha256(codeVerifier));
        const string codeChallengeMethod = "S256";

        // Creates an HttpListener to listen for requests on that redirect URI.
        var http = new HttpListener();
        http.Prefixes.Add(redirectURI);
        Console.WriteLine("Listening..");
        http.Start();

        // Creates the OAuth 2.0 authorization request.
        string authorizationRequestURI = "https://accounts.google.com/o/oauth2/v2/auth";
        string scope = "https://mail.google.com/ https://www.googleapis.com/auth/userinfo.email https://www.googleapis.com/auth/userinfo.profile";

        string authorizationRequest = string.Format("{0}?response_type=code&scope={6}&redirect_uri={1}&client_id={2}&state={3}&code_challenge={4}&code_challenge_method={5}",
            authorizationRequestURI,
            Uri.EscapeDataString(redirectURI),
            clientID,
            state,
            codeChallenge,
            codeChallengeMethod,
            Uri.EscapeDataString(scope)
            );

        // Opens request in the browser.
        System.Diagnostics.Process.Start(authorizationRequest);

        // Waits for the OAuth authorization response.
        var context = http.GetContext();

        // Sends an HTTP response to the browser.
        var response = context.Response;
        string responseString = string.Format("<html><head><meta http-equiv='refresh' content='10;url=https://google.com'></head><body>Please return to the app.</body></html>");
        var buffer = Encoding.UTF8.GetBytes(responseString);
        response.ContentLength64 = buffer.Length;
        using (var responseOutput = response.OutputStream)
            responseOutput.Write(buffer, 0, buffer.Length);
        http.Stop();
        Console.WriteLine("HTTP server stopped.");

        // Checks for errors.
        if (context.Request.QueryString.Get("error") != null)
        {
            Console.WriteLine(String.Format("OAuth authorization error: {0}.", context.Request.QueryString.Get("error")));
            return null;
        }

        if (context.Request.QueryString.Get("code") == null
            || context.Request.QueryString.Get("state") == null)
        {
            Console.WriteLine("Malformed authorization response. " + context.Request.QueryString);
            return null;
        }

        // extracts the code
        var code = context.Request.QueryString.Get("code");
        var incomingState = context.Request.QueryString.Get("state");

        // Compares the receieved state to the expected value, to ensure that
        // this app made the request which resulted in authorization.
        if (incomingState != state)
        {
            Console.WriteLine(String.Format("Received request with invalid state ({0})", incomingState));
            return null;
        }

        Console.WriteLine("Authorization code: " + code);
        return code;
    }

    static string GetAccessToken(string code, string clientID, string clientSecret, string codeVerifier, string redirectURI)
    {
        Console.WriteLine("Exchanging code for tokens...");

        // builds the  request
        string tokenRequestURI = "https://www.googleapis.com/oauth2/v4/token";
        string tokenRequestBody = string.Format("code={0}&redirect_uri={1}&client_id={2}&code_verifier={3}&client_secret={4}&grant_type=authorization_code",
            code,
            Uri.EscapeDataString(redirectURI),
            clientID,
            codeVerifier,
            clientSecret
            );

        // sends the request
        HttpWebRequest tokenRequest = (HttpWebRequest)WebRequest.Create(tokenRequestURI);
        tokenRequest.Method = "POST";
        tokenRequest.ContentType = "application/x-www-form-urlencoded";
        tokenRequest.Accept = "Accept=text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";
        byte[] tokenRequestBytes = Encoding.ASCII.GetBytes(tokenRequestBody);
        tokenRequest.ContentLength = tokenRequestBytes.Length;
        Stream stream = tokenRequest.GetRequestStream();
        stream.Write(tokenRequestBytes, 0, tokenRequestBytes.Length);
        stream.Close();

        try
        {
            // gets the response
            WebResponse tokenResponse = tokenRequest.GetResponse();
            StreamReader reader = new StreamReader(tokenResponse.GetResponseStream());
            // reads response body
            string responseText = reader.ReadToEnd();
            Console.WriteLine(responseText);

            // converts to dictionary
            return responseText;
        }
        catch (WebException ex)
        {
            if (ex.Status == WebExceptionStatus.ProtocolError)
            {
                if (ex.Response is HttpWebResponse response)
                {
                    Console.WriteLine("HTTP: " + response.StatusCode);
                    StreamReader reader = new StreamReader(response.GetResponseStream());
                    // reads response body
                    string responseText = reader.ReadToEnd();
                    Console.WriteLine(responseText);
                }
            }
            return null;
        }
    }

    public static void GetuserProfile(string accesstoken)
    {
        string url = "https://www.googleapis.com/oauth2/v1/userinfo?alt=json&access_token=" + accesstoken + "";
        WebRequest request = WebRequest.Create(url);
        request.Credentials = CredentialCache.DefaultCredentials;
        try
        {
            WebResponse response = request.GetResponse();
            Stream dataStream = response.GetResponseStream();
            StreamReader reader = new StreamReader(dataStream);
            string responseFromServer = reader.ReadToEnd();
            reader.Close();
            response.Close();

            Console.WriteLine(responseFromServer);
        }
        catch (WebException ex)
        {
            if (ex.Status == WebExceptionStatus.ProtocolError)
            {
                if (ex.Response is HttpWebResponse response)
                {
                    Console.WriteLine("HTTP: " + response.StatusCode);
                    StreamReader reader = new StreamReader(response.GetResponseStream());
                    // reads response body
                    string responseText = reader.ReadToEnd();
                    Console.WriteLine(responseText);
                }
            }
        }
    }

    public static string RefreshToken(string refreshToken, string clientID, string clientSecret)
    {
        var request = (HttpWebRequest)WebRequest.Create("https://accounts.google.com/o/oauth2/token");
        string postData = string.Format("client_id={0}&client_secret={1}&refresh_token={2}&grant_type=refresh_token", clientID, clientSecret, refreshToken);
        var data = Encoding.ASCII.GetBytes(postData);

        request.Method = "POST";
        request.ContentType = "application/x-www-form-urlencoded";
        request.ContentLength = data.Length;

        using (var stream = request.GetRequestStream())
        {
            stream.Write(data, 0, data.Length);
        }

        var response = (HttpWebResponse)request.GetResponse();
        var responseString = new StreamReader(response.GetResponseStream()).ReadToEnd();

        return responseString;
    }

    private static int GetRandomUnusedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string RandomDataBase64Url(uint length)
    {
        RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider();
        byte[] bytes = new byte[length];
        rng.GetBytes(bytes);
        return Base64UrlEncodeNoPadding(bytes);
    }

    private static byte[] Sha256(string inputStirng)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(inputStirng);
        SHA256Managed sha256 = new SHA256Managed();
        return sha256.ComputeHash(bytes);
    }

    private static string Base64UrlEncodeNoPadding(byte[] buffer)
    {
        string base64 = Convert.ToBase64String(buffer);

        // Converts base64 to base64url.
        base64 = base64.Replace("+", "-");
        base64 = base64.Replace("/", "_");
        // Strips padding.
        base64 = base64.Replace("=", "");

        return base64;
    }
}
