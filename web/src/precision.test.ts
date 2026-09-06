import { expect, expectTypeOf, test } from 'vitest';
import type { components } from './api/schema';
test('wire payload keeps Int64 values as strings across JSON roundtrip', () => {
  const payload = { id: '9223372036854775807', count: '9007199254740993' };
  expect(JSON.parse(JSON.stringify(payload))).toEqual(payload);
  expect(BigInt(payload.count)).toBe(9007199254740993n);
});
test('generated link ID remains a string, never a JavaScript number', () => {
  expectTypeOf<components['schemas']['Link']['id']>().toEqualTypeOf<string>();
});
