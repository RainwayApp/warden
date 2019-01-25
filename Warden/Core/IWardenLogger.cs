namespace Warden.Core
{
    public interface IWardenLogger
    {
        void Error(string message);

        void Debug(string message);

        void Info(string message);
    }
}
