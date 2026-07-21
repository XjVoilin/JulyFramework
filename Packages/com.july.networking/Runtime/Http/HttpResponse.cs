namespace July.Networking
{
    public readonly struct HttpResponse
    {
        public readonly int StatusCode;
        public readonly byte[] Data;
        public readonly string Error;
        public readonly bool IsNetworkError;

        public HttpResponse(int statusCode, byte[] data, string error, bool isNetworkError)
        {
            StatusCode = statusCode;
            Data = data;
            Error = error;
            IsNetworkError = isNetworkError;
        }

        public bool IsHttpOk => StatusCode >= 200 && StatusCode < 300;
        public bool HasBody => Data != null && Data.Length > 0;
        public bool HasResponse => StatusCode > 0;

        public string GetText()
        {
            if (Data == null || Data.Length == 0)
                return string.Empty;
            return System.Text.Encoding.UTF8.GetString(Data);
        }
    }
}
