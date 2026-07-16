using System;
using System.Security.Cryptography;
using System.Text;

namespace LicenseKeyGenerator
{
    class Program
    {
        private const int KeyLength = 16;
        private const string KeyChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        static void Main(string[] args)
        {
            Console.WriteLine("=== NewEastSide 卡密生成器 ===");
            Console.WriteLine();

            if (args.Length > 0 && int.TryParse(args[0], out int days))
            {
                GenerateAndSave(days);
            }
            else
            {
                Console.Write("请输入有效天数 (如 1, 7, 30, 90, 365): ");
                var input = Console.ReadLine();
                if (int.TryParse(input, out days) && days > 0)
                {
                    GenerateAndSave(days);
                }
                else
                {
                    Console.WriteLine("输入无效，已退出。");
                }
            }
        }

        static void GenerateAndSave(int days)
        {
            var key = GenerateKey();
            var hash = ComputeHash(key);
            var prefix = key.Substring(0, 4);

            Console.WriteLine();
            Console.WriteLine($"生成卡密: {key}");
            Console.WriteLine($"有效天数: {days} 天");
            Console.WriteLine($"SHA256哈希: {hash}");
            Console.WriteLine($"前缀: {prefix}");

            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var filePath = System.IO.Path.Combine(desktopPath, "license_key.txt");

            var content = $"NewEastSide 卡密\n";
            content += $"====================\n";
            content += $"卡密: {key}\n";
            content += $"有效天数: {days} 天\n";
            content += $"生成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n";
            content += $"\n";
            content += $"请在数据库中插入以下记录:\n";
            content += $"key_hash: {hash}\n";
            content += $"key_prefix: {prefix}\n";
            content += $"duration_days: {days}\n";

            System.IO.File.WriteAllText(filePath, content, Encoding.UTF8);
            Console.WriteLine();
            Console.WriteLine($"卡密已保存到桌面: {filePath}");
            Console.WriteLine("按任意键退出...");
            Console.ReadKey();
        }

        static string GenerateKey()
        {
            var randomBytes = new byte[KeyLength];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(randomBytes);

            var keyBuilder = new StringBuilder();
            for (int i = 0; i < KeyLength; i++)
            {
                keyBuilder.Append(KeyChars[randomBytes[i] % KeyChars.Length]);
            }

            var rawKey = keyBuilder.ToString();
            return $"{rawKey.Substring(0, 4)}-{rawKey.Substring(4, 4)}-{rawKey.Substring(8, 4)}-{rawKey.Substring(12, 4)}";
        }

        static string ComputeHash(string key)
        {
            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(key.ToUpper().Replace("-", ""));
            var hash = sha256.ComputeHash(bytes);
            return Convert.ToHexString(hash).ToLower();
        }
    }
}
