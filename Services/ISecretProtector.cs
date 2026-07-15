namespace NetworkHealthMonitor.Services;

public interface ISecretProtector
{
    string Protect(string plainText);

    string Unprotect(string protectedText);
}
