import { createHmac, timingSafeEqual } from "node:crypto";

/** Verifies the HMAC-SHA256 signature on a FinCopy webhook request. */
export class FinWebhook {
  private static readonly allowedSkewSeconds = 300;

  /** Verify X-FinCopy-Signature over the timestamp and the exact raw request body. */
  static verify(secret: string, timestamp: string | number, body: string | Uint8Array, signature: string): boolean {
    if (!secret || !signature) return false;
    const timestampText = String(timestamp);
    if (!/^\d+$/.test(timestampText)) return false;
    const seconds = Number(timestampText);
    if (!Number.isSafeInteger(seconds)) return false;
    const now = Math.floor(Date.now() / 1000);
    if (now - seconds > this.allowedSkewSeconds || seconds - now > this.allowedSkewSeconds) return false;

    const suppliedHex = signature.startsWith("sha256=") ? signature.slice(7) : signature;
    if (!/^[a-fA-F0-9]{64}$/.test(suppliedHex)) return false;
    const supplied = Buffer.from(suppliedHex, "hex");
    const signedBody = Buffer.isBuffer(body) ? body : Buffer.from(body);
    const expected = createHmac("sha256", Buffer.from(secret, "utf8"))
      .update(Buffer.from(`${timestampText}.`, "utf8"))
      .update(signedBody)
      .digest();
    return supplied.length === expected.length && timingSafeEqual(supplied, expected);
  }
}
