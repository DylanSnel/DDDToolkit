// Builds sample/ and writes what the DDDToolkit generators produced for it to src/data/generated.json.
// Run it after a change to a generator or to the sample: npm run generate-sample
//
// The homepage shows this file, so the code on it is the generators' real output rather than an
// imitation that drifts. The build needs the .NET SDK; the site build does not, because the JSON is
// committed.

import { execFileSync } from 'node:child_process';
import { readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

const website = join(dirname(fileURLToPath(import.meta.url)), '..');
const sample = join(website, 'sample');
const output = join(sample, 'obj', 'generated');

// The generators only emit what they generate now, so stale files from an older build must go.
rmSync(output, { recursive: true, force: true });
execFileSync('dotnet', ['build', sample, '--no-incremental', '-nologo', '-v', 'q', '-p:TreatWarningsAsErrors=true'], { stdio: 'inherit' });

const packages = {
  'DDDToolkit.Analyzers': 'DDDToolkit',
  'DDDToolkit.EntityFramework.Analyzers': 'DDDToolkit.EntityFramework',
  'DDDToolkit.HotChocolate.Analyzers': 'DDDToolkit.HotChocolate',
};

// Fully qualified names are what keeps generated code compiling in any namespace, and all they do on
// a web page is double the width of every line. The page says they were taken out.
const readable = (code) => code.replace(/^﻿/, '').replace(/global::/g, '').replace(/\r\n/g, '\n').trimEnd();

function walk(directory) {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) =>
    entry.isDirectory() ? walk(join(directory, entry.name)) : [join(directory, entry.name)],
  );
}

const files = walk(output)
  .filter((path) => path.endsWith('.cs'))
  .map((path) => {
    const [assembly, generator, file] = relative(output, path).split(/[\\/]/);
    const code = readable(readFileSync(path, 'utf8'));
    return {
      package: packages[assembly] ?? assembly,
      generator: generator.split('.').pop(),
      // The hash keeps two types of one name apart on disk; the page has no such types.
      name: file.replace(/\.[0-9a-f]{8}\.g\.cs$/, '.g.cs'),
      lines: code.split('\n').length,
      code,
    };
  })
  .sort((a, b) => Object.values(packages).indexOf(a.package) - Object.values(packages).indexOf(b.package) || a.name.localeCompare(b.name));

const inputs = ['Order.cs', 'OrderLine.cs', 'Address.cs', 'OrderPlaced.cs'].map((name) => {
  const code = readable(readFileSync(join(sample, name), 'utf8'));
  return { name, lines: code.split('\n').length, code };
});

writeFileSync(join(website, 'src', 'data', 'generated.json'), JSON.stringify({ inputs, files }, null, 2) + '\n');

const written = files.reduce((sum, file) => sum + file.lines, 0);
const read = inputs.reduce((sum, file) => sum + file.lines, 0);
console.log(`${read} lines in, ${written} lines generated in ${files.length} files.`);
