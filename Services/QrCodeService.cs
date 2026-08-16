using System.Globalization;
using System.Text;
using QRCoder;
using CommunityFootballClubManager.Models;

namespace CommunityFootballClubManager.Services;

public sealed class QrCodeService
{
    public byte[]? CreatePaymentQr(ClubProfile club, TuitionInvoice invoice)
    {
        if (string.IsNullOrWhiteSpace(club.BankBin)
            || string.IsNullOrWhiteSpace(club.BankAccountNumber)
            || invoice.AmountVnd <= 0)
        {
            return null;
        }

        var payload = CreateVietQrPayload(
            club.BankBin,
            club.BankAccountNumber,
            invoice.AmountVnd,
            invoice.PaymentContent);

        return PngByteQRCodeHelper.GetQRCode(payload, QRCodeGenerator.ECCLevel.Q, 12);
    }

    public string CreateVietQrPayload(
        string bankBin,
        string accountNumber,
        long amountVnd,
        string paymentContent)
    {
        var beneficiary = Tlv("00", DigitsOnly(bankBin))
            + Tlv("01", DigitsOnly(accountNumber));
        var merchantAccount = Tlv("00", "A000000727")
            + Tlv("01", beneficiary)
            + Tlv("02", "QRIBFTTA");

        var normalizedContent = RemoveDiacritics(paymentContent).Trim();
        if (Encoding.UTF8.GetByteCount(normalizedContent) > 25)
        {
            while (Encoding.UTF8.GetByteCount(normalizedContent) > 25)
            {
                normalizedContent = normalizedContent[..^1];
            }
        }

        var payload = Tlv("00", "01")
            + Tlv("01", "12")
            + Tlv("38", merchantAccount)
            + Tlv("53", "704")
            + Tlv("54", amountVnd.ToString(CultureInfo.InvariantCulture))
            + Tlv("58", "VN")
            + Tlv("62", Tlv("08", normalizedContent))
            + "6304";

        return payload + ComputeCrc16(payload);
    }

    private static string Tlv(string id, string value)
    {
        var length = Encoding.UTF8.GetByteCount(value);
        return $"{id}{length:00}{value}";
    }

    private static string DigitsOnly(string value) =>
        new(value.Where(char.IsDigit).ToArray());

    private static string RemoveDiacritics(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character switch
                {
                    'đ' => 'd',
                    'Đ' => 'D',
                    _ => character
                });
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string ComputeCrc16(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        ushort crc = 0xFFFF;

        foreach (var value in bytes)
        {
            crc ^= (ushort)(value << 8);
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (ushort)((crc & 0x8000) != 0
                    ? (crc << 1) ^ 0x1021
                    : crc << 1);
            }
        }

        return crc.ToString("X4", CultureInfo.InvariantCulture);
    }
}
