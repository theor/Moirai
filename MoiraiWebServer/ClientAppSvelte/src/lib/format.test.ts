import { describe, expect, it } from 'vitest';
import { formatValueText, humanLabel, unquote } from './format';

describe('humanLabel', () => {
  it('spaces underscores and capitalises the first letter only', () => {
    expect(humanLabel('first_name')).toBe('First name');
    expect(humanLabel('Family_name')).toBe('Family name');
  });
  it('drops the quotes a @display label keeps', () => {
    expect(humanLabel("'Children'")).toBe('Children');
    expect(humanLabel("'Items owned'")).toBe('Items owned');
  });
});

describe('unquote', () => {
  it('strips one pair of surrounding quotes and nothing else', () => {
    expect(unquote("'faith'")).toBe('faith');
    expect(unquote("it's")).toBe("it's");
  });
});

describe('formatValueText', () => {
  it('shows an enum member by its own name', () => {
    expect(formatValueText('Title.King', true)).toBe('King');
    expect(formatValueText('GodTypes.The_Sea', true)).toBe('The Sea');
  });
  it('reads bare booleans and null as words when they are the whole value', () => {
    expect(formatValueText('null', true)).toBe('—');
    expect(formatValueText('false', true)).toBe('no');
    expect(formatValueText('true', true)).toBe('yes');
  });
  it('leaves the same words alone between two links', () => {
    expect(formatValueText(' null ', false)).toBe(' null ');
  });
  it('drops the arrow out of nothing, and draws the rest as an arrow', () => {
    expect(formatValueText('null -> 50%', true)).toBe('50%');
    expect(formatValueText('Age.Child -> Age.Young', true)).toBe('Child → Young');
    expect(formatValueText('30% -> 35%', true)).toBe('30% → 35%');
    expect(formatValueText('true -> false', true)).toBe('yes → no');
  });
  it('drops the arrow when the target is a link that follows', () => {
    // "null -> <#4>The Land</>" arrives as a text piece then a link.
    expect(formatValueText('null -> ', false)).toBe('');
    expect(formatValueText(' -> ', false)).toBe(' → ');
  });
  it('does not mistake ordinary text for an enum', () => {
    expect(formatValueText('St. Anne', true)).toBe('St. Anne');
    expect(formatValueText('1.5', true)).toBe('1.5');
  });
});
