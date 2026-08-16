const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');

const extensionRoot = path.join(__dirname, '..');
const grammar = JSON.parse(fs.readFileSync(
  path.join(extensionRoot, 'syntaxes', 'lucent.tmLanguage.json'),
  'utf8'));
const manifest = JSON.parse(fs.readFileSync(
  path.join(extensionRoot, 'package.json'),
  'utf8'));

test('grammar scopes Lucent declarations, modifiers, properties, and values', () => {
  const repository = grammar.repository;
  assert.match(repository.declarations.patterns[2].match, /component/);
  assert.match(repository.declarations.patterns[4].match, /Computed/);
  assert.match(repository.modifiers.patterns[0].match, /internal/);
  assert.match(repository.properties.patterns[0].name, /attribute-name/);
  assert.match(repository.constants.patterns[0].match, /false/);
  assert.match(repository.constants.patterns[1].name, /numeric/);
  assert.equal(repository.foreach.patterns[0].captures['3'].name, 'storage.type.implicit.cs');
  assert.equal(repository.foreach.patterns[0].captures['5'].name, 'keyword.control.loop.in.cs');
});

test('grammar falls through to standard C# scopes', () => {
  assert.ok(grammar.patterns.some(pattern => pattern.include === 'source.cs'));
  const contribution = manifest.contributes.grammars.find(
    item => item.language === 'lucent');
  assert.equal(contribution.embeddedLanguages['source.cs'], 'csharp');
});
