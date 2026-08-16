const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const extensionRoot = path.resolve(__dirname, '..');

function readJson(relativePath) {
    return JSON.parse(fs.readFileSync(path.join(extensionRoot, relativePath), 'utf8'));
}

test('manifest registers .lui and the Lucent grammar', () => {
    const manifest = readJson('package.json');
    const language = manifest.contributes.languages.find(
        (entry) => entry.id === 'lucent');
    const grammar = manifest.contributes.grammars.find(
        (entry) => entry.language === 'lucent');

    assert.ok(language);
    assert.deepEqual(language.extensions, ['.lui']);
    assert.equal(grammar.scopeName, 'source.lucent');
    assert.equal(grammar.path, './syntaxes/lucent.tmLanguage.json');
});

test('manifest packages the language client runtime', () => {
    const manifest = readJson('package.json');

    assert.ok(manifest.files.includes('node_modules'));
    assert.equal(manifest.dependencies['vscode-languageclient'], '10.1.0');
    assert.ok(manifest.contributes.commands.some(
        command => command.command === 'lucent.showLanguageServerOutput'));
    assert.deepEqual(
        manifest.contributes.configuration.properties['lucent.languageServer.trace'].enum,
        ['off', 'messages', 'verbose']);
});

test('grammar and language configuration are valid JSON with Lucent constructs', () => {
    const grammar = readJson('syntaxes/lucent.tmLanguage.json');
    const configuration = readJson('language-configuration.json');

    assert.equal(grammar.scopeName, 'source.lucent');
    assert.match(
        grammar.repository.declarations.patterns[2].match,
        /component/);
    assert.match(
        grammar.repository.keywords.patterns[0].match,
        /keyed/);
    assert.match(
        grammar.repository.properties.patterns[0].match,
        /A-Za-z/);
    assert.deepEqual(configuration.brackets[0], ['{', '}']);
    assert.equal(configuration.comments.lineComment, '//');
});
