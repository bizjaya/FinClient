/** One server-sent event from the FinCopy event stream. */
export interface FinStreamEvent { event: string; data: string }

/**
 * Reads server-sent event lines one at a time. A blank line ends an event; comment lines (": ping") are skipped; several
 * `data:` lines join with a newline; an event without a name is a "message"; an event with no data line is not delivered,
 * as in the EventSource standard; other fields (id, retry) are ignored.
 */
export class FinSseParser {
  private eventName: string | null = null;
  private data: string[] = [];

  /** Adds one line (without its line break). Returns the event the line completed, if any. */
  add(line: string): FinStreamEvent | null {
    if (!line) return this.flush();
    if (line.startsWith(":")) return null;
    const separator = line.indexOf(":");
    const field = separator < 0 ? line : line.slice(0, separator);
    const value = separator < 0 ? "" : line.slice(separator + 1).replace(/^ /, "");
    if (field === "event") this.eventName = value;
    else if (field === "data") this.data.push(value);
    return null;
  }

  /** Ends the stream: an event still being read is returned when it carries data. */
  complete(): FinStreamEvent | null {
    return this.data.length ? this.flush() : null;
  }

  private flush(): FinStreamEvent | null {
    const item = this.data.length ? { event: this.eventName ?? "message", data: this.data.join("\n") } : null;
    this.eventName = null;
    this.data = [];
    return item;
  }
}
