import type { components } from "./generated/schema.js";

/** All wire DTOs generated from the checked-in FinCopy OpenAPI snapshot. */
export type FinCopySchemas = components["schemas"];
export type FinCopyDto<K extends keyof FinCopySchemas> = FinCopySchemas[K];

/** Extracts the PascalCase payload type from one generated response envelope. */
export type FinResponseData<K extends keyof FinCopySchemas> =
  FinCopySchemas[K] extends { data?: infer T } ? NonNullable<T> : never;
