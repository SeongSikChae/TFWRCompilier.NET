#!/usr/bin/env node
/**
 * TypeScript → TFWR IR JSON bridge.
 * Reads a JSON request from argv[2] (file path) or stdin; writes IR JSON to stdout.
 */
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import ts from "typescript";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const DEFAULT_TFWR_DTS = path.resolve(__dirname, "../../../TFWR.Api.Ts/tfwr.d.ts");

const GAME_ENUMS = new Set([
  "Direction",
  "Entities",
  "Items",
  "Grounds",
  "Hats",
  "Unlocks",
  "Leaderboards",
]);

const GAME_BUILTINS = new Set([
  "harvest", "canHarvest", "plant", "swap", "till", "useItem", "clear", "changeHat",
  "move", "canMove", "getPosX", "getPosY", "getWorldSize", "getEntityType", "getGroundType",
  "getWater", "numItems", "getCompanion", "measure", "spawnDrone", "waitFor", "hasFinished",
  "maxDrones", "numDrones", "getTime", "getTickCount", "setExecutionSpeed", "setWorldSize",
  "getCost", "unlock", "numUnlocked", "random", "min", "max", "abs", "print", "quickPrint",
  "doAFlip", "petThePiggy", "leaderboardRun", "range", "len", "str",
]);

function camelOrPascalToSnake(name) {
  if (!name) return name;
  let sb = "";
  for (let i = 0; i < name.length; i++) {
    const c = name[i];
    if (c >= "A" && c <= "Z" && i > 0) {
      const prev = name[i - 1];
      const next = i + 1 < name.length ? name[i + 1] : "";
      const prevUpper = prev >= "A" && prev <= "Z";
      const nextLower = next >= "a" && next <= "z";
      if (!prevUpper || nextLower) sb += "_";
    }
    sb += c.toLowerCase();
  }
  return sb;
}

function moduleNameFromPath(relativePath) {
  return path.basename(relativePath, path.extname(relativePath));
}

function outputPyPath(relativePath) {
  return relativePath.replace(/\\/g, "/").replace(/\.tsx?$/i, ".py");
}

function readRequest() {
  const argPath = process.argv[2];
  if (argPath) {
    return JSON.parse(fs.readFileSync(argPath, "utf8"));
  }
  const raw = fs.readFileSync(0, "utf8");
  return JSON.parse(raw);
}

function main() {
  let request;
  try {
    request = readRequest();
  } catch (e) {
    writeResult({
      diagnostics: [{ severity: "error", message: `Invalid request JSON: ${e.message}` }],
      modules: [],
    });
    process.exit(0);
  }

  const result = lowerRequest(request);
  writeResult(result);
}

function writeResult(result) {
  process.stdout.write(JSON.stringify(result));
}

function lowerRequest(request) {
  const diagnostics = [];
  const files = request.files ?? [];
  const emitTopLevelEntry = !!request.emitTopLevelEntry;
  const tfwrDtsPath = request.tfwrDtsPath
    ? path.resolve(request.tfwrDtsPath)
    : DEFAULT_TFWR_DTS;

  if (files.length === 0) {
    diagnostics.push({ severity: "error", message: "At least one input .ts file is required." });
    return { diagnostics, modules: [] };
  }

  if (!fs.existsSync(tfwrDtsPath)) {
    diagnostics.push({
      severity: "error",
      message: `tfwr.d.ts not found at ${tfwrDtsPath}`,
    });
    return { diagnostics, modules: [] };
  }

  const absFiles = [];
  const pathMap = new Map(); // abs -> relative
  for (const f of files) {
    const abs = path.resolve(f.path);
    if (!fs.existsSync(abs)) {
      diagnostics.push({
        severity: "error",
        message: `Input file not found: ${f.path}`,
        filePath: f.path,
      });
      continue;
    }
    if (!/\.tsx?$/i.test(abs)) {
      diagnostics.push({
        severity: "error",
        message: "Only .ts/.tsx files are accepted for --lang ts.",
        filePath: abs,
      });
      continue;
    }
    absFiles.push(abs);
    pathMap.set(abs, f.relativePath.replace(/\\/g, "/"));
  }

  if (diagnostics.some((d) => d.severity === "error")) {
    return { diagnostics, modules: [] };
  }

  const moduleNames = new Map();
  for (const abs of absFiles) {
    const rel = pathMap.get(abs);
    const mod = moduleNameFromPath(rel);
    if (moduleNames.has(mod)) {
      diagnostics.push({
        severity: "error",
        message: `Duplicate module name '${mod}' from '${rel}' and '${moduleNames.get(mod)}'.`,
        filePath: abs,
      });
    } else {
      moduleNames.set(mod, rel);
    }
  }
  if (diagnostics.some((d) => d.severity === "error")) {
    return { diagnostics, modules: [] };
  }

  // Virtual root for path mapping
  const virtualRoot = path.resolve(path.dirname(absFiles[0]), "__tfwr_virtual__");
  const options = {
    target: ts.ScriptTarget.ES2022,
    module: ts.ModuleKind.ESNext,
    moduleResolution: ts.ModuleResolutionKind.Bundler,
    strict: true,
    noEmit: true,
    skipLibCheck: true,
    baseUrl: virtualRoot,
    paths: { tfwr: [tfwrDtsPath] },
    allowJs: false,
  };

  const host = ts.createCompilerHost(options, true);
  const originalRead = host.readFile.bind(host);
  const originalFileExists = host.fileExists.bind(host);
  host.fileExists = (fileName) => {
    if (fileName.replace(/\\/g, "/").endsWith("/tfwr.d.ts") || fileName === tfwrDtsPath) {
      return fs.existsSync(tfwrDtsPath);
    }
    // path mapping may request weird paths
    if (path.basename(fileName) === "tfwr.d.ts" && fs.existsSync(tfwrDtsPath)) {
      return true;
    }
    return originalFileExists(fileName);
  };
  host.readFile = (fileName) => {
    if (
      fileName === tfwrDtsPath ||
      fileName.replace(/\\/g, "/").endsWith("/tfwr.d.ts") ||
      path.basename(fileName) === "tfwr.d.ts"
    ) {
      return fs.readFileSync(tfwrDtsPath, "utf8");
    }
    return originalRead(fileName);
  };

  const rootNames = [...absFiles, tfwrDtsPath];
  const program = ts.createProgram({ rootNames, options, host });
  const checker = program.getTypeChecker();

  for (const diag of ts.getPreEmitDiagnostics(program)) {
    // Ignore diagnostics from the ambient d.ts itself when unused, still report user errors
    const file = diag.file;
    const message = ts.flattenDiagnosticMessageText(diag.messageText, "\n");
    const severity =
      diag.category === ts.DiagnosticCategory.Error
        ? "error"
        : diag.category === ts.DiagnosticCategory.Warning
          ? "warning"
          : "info";

    // Skip module-not-found for 'tfwr' if our mapping failed oddly — try resolve
    if (file && absFiles.includes(path.resolve(file.fileName))) {
      const start = diag.start ?? 0;
      const lc = file.getLineAndCharacterOfPosition(start);
      diagnostics.push({
        severity,
        message,
        filePath: file.fileName,
        line: lc.line + 1,
        column: lc.character + 1,
      });
    } else if (!file && severity === "error") {
      diagnostics.push({ severity, message });
    }
  }

  // Build export/symbol → module map for cross-file calls
  const symbolModuleMap = new Map(); // symbol -> moduleName
  for (const abs of absFiles) {
    const sf = program.getSourceFile(abs);
    if (!sf) continue;
    const mod = moduleNameFromPath(pathMap.get(abs));
    const collect = (node) => {
      if (ts.isFunctionDeclaration(node) && node.name) {
        const sym = checker.getSymbolAtLocation(node.name);
        if (sym) symbolModuleMap.set(sym, mod);
      }
      if (ts.isClassDeclaration(node) && node.name) {
        const sym = checker.getSymbolAtLocation(node.name);
        if (sym) symbolModuleMap.set(sym, mod);
        for (const member of node.members) {
          if (
            (ts.isMethodDeclaration(member) || ts.isConstructorDeclaration(member)) &&
            member.name &&
            ts.isIdentifier(member.name)
          ) {
            const msym = checker.getSymbolAtLocation(member.name);
            if (msym) symbolModuleMap.set(msym, mod);
          }
        }
      }
      ts.forEachChild(node, collect);
    };
    collect(sf);
  }

  if (diagnostics.some((d) => d.severity === "error")) {
    return { diagnostics, modules: [] };
  }

  const modules = [];
  for (const abs of absFiles) {
    const sf = program.getSourceFile(abs);
    const rel = pathMap.get(abs);
    const modName = moduleNameFromPath(rel);
    const ctx = new LoweringContext({
      checker,
      sourceFile: sf,
      moduleName: modName,
      relativePath: rel,
      emitTopLevelEntry,
      symbolModuleMap,
      diagnostics,
      absFiles,
      pathMap,
      program,
    });
    modules.push(ctx.lowerSourceFile());
  }

  return { diagnostics, modules };
}

class LoweringContext {
  constructor(opts) {
    Object.assign(this, opts);
    this.imports = new Set();
    this.tempCounter = 0;
    this.hoist = null;
    this.tfwrLocals = new Set(); // local names bound from 'tfwr' imports
    this.tfwrNamespace = null; // import * as X from "tfwr"
    this.localEnumAliases = new Map(); // local name -> enum type name
    this.classNames = new Set();
  }

  error(node, message) {
    const start = node?.getStart?.(this.sourceFile) ?? 0;
    const lc = this.sourceFile.getLineAndCharacterOfPosition(start);
    this.diagnostics.push({
      severity: "error",
      message,
      filePath: this.sourceFile.fileName,
      line: lc.line + 1,
      column: lc.character + 1,
    });
  }

  newTemp(prefix) {
    this.tempCounter += 1;
    return `_${prefix}${this.tempCounter}`;
  }

  lowerSourceFile() {
    const module = {
      moduleName: this.moduleName,
      relativeOutputPath: outputPyPath(this.relativePath),
      imports: [],
      functions: [],
      topLevelStatements: [],
      entryFunctionName: null,
    };

    // Collect imports from "tfwr" and relative modules
    for (const stmt of this.sourceFile.statements) {
      if (!ts.isImportDeclaration(stmt)) continue;
      const spec = stmt.moduleSpecifier;
      if (!ts.isStringLiteral(spec)) continue;
      const modText = spec.text;
      if (modText === "tfwr") {
        this.collectTfwrImport(stmt);
        continue;
      }
      if (modText.startsWith(".")) {
        const resolved = this.resolveRelativeModule(modText);
        if (resolved) this.imports.add(resolved);
      }
    }

    // Classes
    for (const stmt of this.sourceFile.statements) {
      if (ts.isClassDeclaration(stmt) && stmt.name) {
        this.classNames.add(stmt.name.text);
        this.lowerClass(stmt, module);
      }
    }

    // Functions
    const topLevelStmts = [];
    for (const stmt of this.sourceFile.statements) {
      if (ts.isFunctionDeclaration(stmt)) {
        this.lowerFunctionDecl(stmt, module);
      } else if (
        !ts.isImportDeclaration(stmt) &&
        !ts.isExportDeclaration(stmt) &&
        !ts.isClassDeclaration(stmt) &&
        !ts.isInterfaceDeclaration(stmt) &&
        !ts.isTypeAliasDeclaration(stmt) &&
        !ts.isEnumDeclaration(stmt)
      ) {
        // top-level statements
        if (ts.isExpressionStatement(stmt) || ts.isVariableStatement(stmt) ||
            ts.isIfStatement(stmt) || ts.isWhileStatement(stmt) || ts.isForStatement(stmt) ||
            ts.isForOfStatement(stmt) || ts.isBlock(stmt)) {
          topLevelStmts.push(...this.lowerStatement(stmt));
        } else if (ts.isExportAssignment(stmt)) {
          // ignore
        } else {
          // try as statement
          try {
            topLevelStmts.push(...this.lowerStatement(stmt));
          } catch {
            this.error(stmt, `Unsupported top-level statement: ${ts.SyntaxKind[stmt.kind]}`);
          }
        }
      }
    }

    if (topLevelStmts.length > 0) {
      if (this.emitTopLevelEntry) {
        module.topLevelStatements.push(...topLevelStmts);
      } else {
        const fn = { name: "main", parameters: [], body: topLevelStmts };
        module.functions.push(fn);
        module.entryFunctionName = "main";
      }
    }

    for (const imp of [...this.imports].sort()) {
      if (imp !== this.moduleName) module.imports.push(imp);
    }

    if (this.emitTopLevelEntry && module.functions.length > 0) {
      this.error(
        this.sourceFile,
        "--toplevel emits entry code without def, but this module still has functions. " +
          "Remove other methods/constructors or unlock Unlocks.Functions.",
      );
    }

    return module;
  }

  collectTfwrImport(stmt) {
    const clause = stmt.importClause;
    if (!clause) return;
    if (clause.name) {
      this.tfwrNamespace = clause.name.text;
    }
    if (clause.namedBindings) {
      if (ts.isNamespaceImport(clause.namedBindings)) {
        this.tfwrNamespace = clause.namedBindings.name.text;
      } else if (ts.isNamedImports(clause.namedBindings)) {
        for (const el of clause.namedBindings.elements) {
          const imported = (el.propertyName ?? el.name).text;
          const local = el.name.text;
          if (GAME_ENUMS.has(imported)) {
            this.localEnumAliases.set(local, imported);
          } else if (GAME_BUILTINS.has(imported) || imported === "Drone") {
            this.tfwrLocals.add(local);
            // map local -> original for snake conversion of original
            if (!this._tfwrLocalToOriginal) this._tfwrLocalToOriginal = new Map();
            this._tfwrLocalToOriginal.set(local, imported);
          } else {
            this.tfwrLocals.add(local);
            if (!this._tfwrLocalToOriginal) this._tfwrLocalToOriginal = new Map();
            this._tfwrLocalToOriginal.set(local, imported);
          }
        }
      }
    }
  }

  resolveRelativeModule(modText) {
    const base = path.dirname(this.sourceFile.fileName);
    const candidates = [
      path.resolve(base, modText + ".ts"),
      path.resolve(base, modText + ".tsx"),
      path.resolve(base, modText),
    ];
    for (const c of candidates) {
      if (this.pathMap.has(c)) {
        return moduleNameFromPath(this.pathMap.get(c));
      }
      // try normalize
      for (const [abs, rel] of this.pathMap) {
        if (path.resolve(abs) === path.resolve(c)) {
          return moduleNameFromPath(rel);
        }
      }
    }
    // basename fallback
    const name = path.basename(modText);
    return name;
  }

  isEntryFunction(node) {
    if (!node.name) return false;
    if (node.name.text === "main") return true;
    const text = node.getFullText(this.sourceFile);
    // JSDoc @tfwrEntry on the node
    const ranges = ts.getJSDocCommentsAndTags(node);
    for (const r of ranges) {
      if (ts.isJSDoc(r) && r.tags) {
        for (const tag of r.tags) {
          if (tag.tagName.text.toLowerCase() === "tfwrentry") return true;
        }
      }
    }
    // also check leading comment
    const commentRanges = ts.getLeadingCommentRanges(this.sourceFile.text, node.pos);
    if (commentRanges) {
      for (const cr of commentRanges) {
        const c = this.sourceFile.text.slice(cr.pos, cr.end);
        if (/@tfwrEntry\b/i.test(c)) return true;
      }
    }
    return false;
  }

  lowerFunctionDecl(node, module) {
    if (!node.body) {
      // overload / ambient
      return;
    }
    const isEntry = this.isEntryFunction(node);
    const name = isEntry && node.name?.text === "main" ? "main" : (node.name?.text ?? "fn");
    const params = (node.parameters ?? []).map((p) => ({
      name: p.name.getText(),
      defaultValue: p.initializer ? this.lowerExpression(p.initializer) : null,
    }));

    this.hoist = [];
    const body = this.lowerBlockStatements(node.body);
    const stmts = [...this.hoist, ...body];
    this.hoist = null;

    if (isEntry && this.emitTopLevelEntry) {
      if (params.length > 0) {
        this.error(node, "--toplevel entry function must have no parameters.");
      }
      module.topLevelStatements.push(...stmts);
      return;
    }

    const fn = { name, parameters: params.map((p) => ({
      name: p.name,
      defaultValue: p.defaultValue,
    })), body: stmts };
    module.functions.push(fn);
    if (isEntry) {
      module.entryFunctionName = name;
    }
  }

  lowerClass(node, module) {
    const className = node.name.text;
    let ctor = null;
    const methods = [];

    for (const member of node.members) {
      if (ts.isConstructorDeclaration(member)) {
        ctor = member;
      } else if (ts.isMethodDeclaration(member) && member.name && ts.isIdentifier(member.name)) {
        methods.push(member);
      } else if (ts.isPropertyDeclaration(member)) {
        // fields handled in ctor / _new
      } else if (ts.isGetAccessorDeclaration(member) || ts.isSetAccessorDeclaration(member)) {
        this.error(member, "Getters/setters are not supported in v1; use fields.");
      }
    }

    // Collect field names
    const fields = [];
    for (const member of node.members) {
      if (ts.isPropertyDeclaration(member) && member.name && ts.isIdentifier(member.name)) {
        fields.push(member.name.text);
      }
    }

    // Parameter properties from constructor
    if (ctor) {
      for (const p of ctor.parameters) {
        const isParamProp = p.modifiers?.some(
          (m) =>
            m.kind === ts.SyntaxKind.PublicKeyword ||
            m.kind === ts.SyntaxKind.PrivateKeyword ||
            m.kind === ts.SyntaxKind.ProtectedKeyword ||
            m.kind === ts.SyntaxKind.ReadonlyKeyword,
        );
        if (isParamProp && ts.isIdentifier(p.name)) {
          if (!fields.includes(p.name.text)) fields.push(p.name.text);
        }
      }
    }

    // _new
    const newParams = [];
    if (ctor) {
      for (const p of ctor.parameters) {
        newParams.push({
          name: ts.isIdentifier(p.name) ? p.name.text : p.name.getText(),
          defaultValue: p.initializer ? this.lowerExpression(p.initializer) : null,
        });
      }
    } else {
      for (const f of fields) {
        newParams.push({ name: f, defaultValue: null });
      }
    }

    const newBody = [];
    // Build instance dict
    const entries = [];
    if (ctor) {
      for (const p of ctor.parameters) {
        if (!ts.isIdentifier(p.name)) continue;
        const isParamProp = p.modifiers?.some(
          (m) =>
            m.kind === ts.SyntaxKind.PublicKeyword ||
            m.kind === ts.SyntaxKind.PrivateKeyword ||
            m.kind === ts.SyntaxKind.ProtectedKeyword ||
            m.kind === ts.SyntaxKind.ReadonlyKeyword,
        );
        if (isParamProp || fields.includes(p.name.text)) {
          entries.push({
            key: { kind: "LiteralExpr", text: JSON.stringify(p.name.text) },
            value: { kind: "NameExpr", name: p.name.text },
          });
        }
      }
    }
    for (const f of fields) {
      if (!entries.some((e) => e.key.text === JSON.stringify(f))) {
        // will assign in ctor body or from param
        if (!ctor) {
          entries.push({
            key: { kind: "LiteralExpr", text: JSON.stringify(f) },
            value: { kind: "NameExpr", name: f },
          });
        }
      }
    }

    newBody.push({
      kind: "AssignStmt",
      target: { kind: "NameExpr", name: "self" },
      value: { kind: "DictExpr", entries },
    });

    if (ctor?.body) {
      this.hoist = [];
      // lower ctor body with this → self dict
      this._inCtor = true;
      this._currentClass = className;
      const bodyStmts = this.lowerBlockStatements(ctor.body);
      newBody.push(...this.hoist, ...bodyStmts);
      this.hoist = null;
      this._inCtor = false;
    }

    newBody.push({ kind: "ReturnStmt", value: { kind: "NameExpr", name: "self" } });

    module.functions.push({
      name: `${className}_new`,
      parameters: newParams,
      body: newBody.length ? newBody : [{ kind: "PassStmt" }],
    });

    for (const method of methods) {
      const methodName = method.name.text;
      const isStatic = method.modifiers?.some((m) => m.kind === ts.SyntaxKind.StaticKeyword);
      const isEntry = this.isEntryFunction(method);

      const params = [];
      if (!isStatic) {
        params.push({ name: "self", defaultValue: null });
      }
      for (const p of method.parameters) {
        params.push({
          name: ts.isIdentifier(p.name) ? p.name.text : p.name.getText(),
          defaultValue: p.initializer ? this.lowerExpression(p.initializer) : null,
        });
      }

      this.hoist = [];
      this._currentClass = className;
      this._inInstanceMethod = !isStatic;
      const body = method.body ? this.lowerBlockStatements(method.body) : [];
      const stmts = [...this.hoist, ...body];
      this.hoist = null;
      this._inInstanceMethod = false;

      let fnName;
      if (isEntry && methodName === "main") {
        fnName = "main";
      } else if (isStatic && isEntry && methodName === "main") {
        fnName = "main";
      } else {
        fnName = `${className}_${methodName}`;
      }

      if (isEntry && this.emitTopLevelEntry) {
        if (params.filter((p) => p.name !== "self").length > 0 && isStatic) {
          // main with params
        }
        if (isStatic) {
          module.topLevelStatements.push(...stmts);
          continue;
        }
      }

      module.functions.push({ name: fnName, parameters: params, body: stmts });
      if (isEntry && !this.emitTopLevelEntry) {
        module.entryFunctionName = fnName;
      }
    }

    this._currentClass = null;
  }

  lowerBlockStatements(block) {
    const out = [];
    for (const stmt of block.statements) {
      out.push(...this.lowerStatement(stmt));
    }
    return out;
  }

  lowerStatement(stmt) {
    if (ts.isBlock(stmt)) {
      return this.lowerBlockStatements(stmt);
    }
    if (ts.isExpressionStatement(stmt)) {
      return this.lowerExpressionStatement(stmt.expression);
    }
    if (ts.isVariableStatement(stmt)) {
      return this.lowerVariableStatement(stmt);
    }
    if (ts.isIfStatement(stmt)) {
      return [this.lowerIf(stmt)];
    }
    if (ts.isWhileStatement(stmt)) {
      return this.lowerWhile(stmt);
    }
    if (ts.isForStatement(stmt)) {
      return this.lowerFor(stmt);
    }
    if (ts.isForOfStatement(stmt)) {
      return this.lowerForOf(stmt);
    }
    if (ts.isReturnStatement(stmt)) {
      return [{
        kind: "ReturnStmt",
        value: stmt.expression ? this.lowerExpression(stmt.expression) : null,
      }];
    }
    if (ts.isBreakStatement(stmt)) {
      return [{ kind: "BreakStmt" }];
    }
    if (ts.isContinueStatement(stmt)) {
      return [{ kind: "ContinueStmt" }];
    }
    if (ts.isEmptyStatement(stmt)) {
      return [];
    }
    if (ts.isTryStatement(stmt)) {
      this.error(stmt, "try/catch is not supported.");
      return [{ kind: "PassStmt" }];
    }
    if (ts.isThrowStatement(stmt)) {
      this.error(stmt, "throw is not supported.");
      return [{ kind: "PassStmt" }];
    }
    if (ts.isSwitchStatement(stmt)) {
      return this.lowerSwitch(stmt);
    }

    this.error(stmt, `Unsupported statement: ${ts.SyntaxKind[stmt.kind]}`);
    return [{ kind: "PassStmt" }];
  }

  lowerExpressionStatement(expr) {
    const saved = this.hoist;
    this.hoist = [];
    // handle ++/--
    if (ts.isPrefixUnaryExpression(expr) || ts.isPostfixUnaryExpression(expr)) {
      if (expr.operator === ts.SyntaxKind.PlusPlusToken || expr.operator === ts.SyntaxKind.MinusMinusToken) {
        const op = expr.operator === ts.SyntaxKind.PlusPlusToken ? "+" : "-";
        const target = this.lowerExpression(expr.operand);
        const stmts = [
          ...this.hoist,
          {
            kind: "AugAssignStmt",
            target,
            op,
            value: { kind: "LiteralExpr", text: "1" },
          },
        ];
        this.hoist = saved;
        return stmts;
      }
    }
    if (ts.isBinaryExpression(expr) && expr.operatorToken.kind === ts.SyntaxKind.EqualsToken) {
      const value = this.lowerExpression(expr.right);
      const target = this.lowerLValue(expr.left);
      const stmts = [...this.hoist, { kind: "AssignStmt", target, value }];
      this.hoist = saved;
      return stmts;
    }
    // compound assign
    if (ts.isBinaryExpression(expr) && isCompoundAssign(expr.operatorToken.kind)) {
      const op = compoundOp(expr.operatorToken.kind);
      const value = this.lowerExpression(expr.right);
      const target = this.lowerLValue(expr.left);
      const stmts = [...this.hoist, { kind: "AugAssignStmt", target, op, value }];
      this.hoist = saved;
      return stmts;
    }

    const e = this.lowerExpression(expr);
    const stmts = [...this.hoist, { kind: "ExprStmt", expression: e }];
    this.hoist = saved;
    return stmts;
  }

  lowerVariableStatement(stmt) {
    const out = [];
    const saved = this.hoist;
    this.hoist = [];
    for (const decl of stmt.declarationList.declarations) {
      if (!ts.isIdentifier(decl.name) && !ts.isObjectBindingPattern(decl.name) && !ts.isArrayBindingPattern(decl.name)) {
        this.error(decl, "Unsupported binding pattern.");
        continue;
      }
      if (ts.isIdentifier(decl.name)) {
        const value = decl.initializer
          ? this.lowerExpression(decl.initializer)
          : { kind: "LiteralExpr", text: "None" };
        out.push(...this.hoist);
        this.hoist = [];
        out.push({
          kind: "AssignStmt",
          target: { kind: "NameExpr", name: decl.name.text },
          value,
        });
      } else {
        this.error(decl, "Destructuring declarations are only supported in for-of.");
      }
    }
    this.hoist = saved;
    return out;
  }

  lowerIf(stmt) {
    const saved = this.hoist;
    this.hoist = [];
    const condition = this.lowerExpression(stmt.expression);
    const pre = [...this.hoist];
    this.hoist = [];

    const thenBody = this.lowerStatement(stmt.thenStatement);
    let elseBody = null;
    const elifs = [];

    let elseStmt = stmt.elseStatement;
    while (elseStmt && ts.isIfStatement(elseStmt)) {
      this.hoist = [];
      const cond = this.lowerExpression(elseStmt.expression);
      const elifPre = [...this.hoist];
      this.hoist = [];
      const body = this.lowerStatement(elseStmt.thenStatement);
      // if condition had hoist, wrap — for simplicity prepend assign temps into body via outer
      if (elifPre.length) {
        // hoist elif conditions are tricky; use nested if fallback only if needed
        // For v1, conditions shouldn't need hoist often; if they do, prepend to a wrapper
        elifs.push({ condition: cond, body: [...elifPre, ...body] });
      } else {
        elifs.push({ condition: cond, body });
      }
      elseStmt = elseStmt.elseStatement;
    }
    if (elseStmt) {
      elseBody = this.lowerStatement(elseStmt);
    }

    this.hoist = saved;
    const ifStmt = {
      kind: "IfStmt",
      condition,
      thenBody,
      elifs,
      elseBody,
    };
    if (pre.length) {
      // return pre + if as multiple — caller expects single IfStmt from lowerIf
      // Store pre in then... actually return via lowerStatement path
      this._pendingPre = pre;
    }
    return ifStmt;
  }

  lowerWhile(stmt) {
    const saved = this.hoist;
    this.hoist = [];
    const condition = this.lowerExpression(stmt.expression);
    const condHoist = [...this.hoist];
    this.hoist = [];
    const body = this.lowerStatement(stmt.statement);
    this.hoist = saved;

    if (condHoist.length === 0) {
      return [{ kind: "WhileStmt", condition, body }];
    }
    // while True: hoist; if not cond: break; body
    return [{
      kind: "WhileStmt",
      condition: { kind: "LiteralExpr", text: "True" },
      body: [
        ...condHoist,
        {
          kind: "IfStmt",
          condition: { kind: "UnaryExpr", op: "not", operand: condition },
          thenBody: [{ kind: "BreakStmt" }],
          elifs: [],
          elseBody: null,
        },
        ...body,
      ],
    }];
  }

  lowerFor(stmt) {
    // for (let i = 0; i < n; i++) → for i in range(n)
    const init = stmt.initializer;
    const cond = stmt.condition;
    const incr = stmt.incrementor;

    let varName = null;
    let start = null;
    if (init && ts.isVariableDeclarationList(init) && init.declarations.length === 1) {
      const d = init.declarations[0];
      if (ts.isIdentifier(d.name) && d.initializer) {
        varName = d.name.text;
        start = this.lowerExpression(d.initializer);
      }
    }

    let end = null;
    if (
      varName &&
      cond &&
      ts.isBinaryExpression(cond) &&
      ts.isIdentifier(cond.left) &&
      cond.left.text === varName &&
      (cond.operatorToken.kind === ts.SyntaxKind.LessThanToken ||
        cond.operatorToken.kind === ts.SyntaxKind.LessThanEqualsToken)
    ) {
      end = this.lowerExpression(cond.right);
      if (cond.operatorToken.kind === ts.SyntaxKind.LessThanEqualsToken) {
        end = {
          kind: "BinaryExpr",
          left: end,
          op: "+",
          right: { kind: "LiteralExpr", text: "1" },
        };
      }
    }

    let stepOk = false;
    if (
      incr &&
      varName &&
      ((ts.isPostfixUnaryExpression(incr) &&
        incr.operator === ts.SyntaxKind.PlusPlusToken &&
        ts.isIdentifier(incr.operand) &&
        incr.operand.text === varName) ||
        (ts.isPrefixUnaryExpression(incr) &&
          incr.operator === ts.SyntaxKind.PlusPlusToken &&
          ts.isIdentifier(incr.operand) &&
          incr.operand.text === varName) ||
        (ts.isBinaryExpression(incr) &&
          incr.operatorToken.kind === ts.SyntaxKind.PlusEqualsToken &&
          ts.isIdentifier(incr.left) &&
          incr.left.text === varName &&
          ts.isNumericLiteral(incr.right) &&
          incr.right.text === "1"))
    ) {
      stepOk = true;
    }

    if (varName && start && end && stepOk) {
      const startLit =
        start.kind === "LiteralExpr" && start.text === "0" ? null : start;
      const args = startLit
        ? [start, end]
        : [end];
      const iterable = {
        kind: "CallExpr",
        callee: { kind: "NameExpr", name: "range" },
        arguments: args,
      };
      const body = this.lowerStatement(stmt.statement);
      return [{ kind: "ForStmt", variable: varName, iterable, body }];
    }

    this.error(stmt, "Only C-style for loops of the form for (i = 0; i < n; i++) are supported.");
    return [{ kind: "PassStmt" }];
  }

  lowerForOf(stmt) {
    let variable;
    if (ts.isVariableDeclarationList(stmt.initializer)) {
      const d = stmt.initializer.declarations[0];
      if (ts.isIdentifier(d.name)) {
        variable = d.name.text;
      } else if (ts.isArrayBindingPattern(d.name)) {
        const names = d.name.elements.map((e) =>
          ts.isBindingElement(e) && ts.isIdentifier(e.name) ? e.name.text : "_",
        );
        variable = names.join(", ");
      } else {
        this.error(stmt, "Unsupported for-of binding.");
        return [{ kind: "PassStmt" }];
      }
    } else if (ts.isIdentifier(stmt.initializer)) {
      variable = stmt.initializer.text;
    } else {
      this.error(stmt, "Unsupported for-of initializer.");
      return [{ kind: "PassStmt" }];
    }

    const iterable = this.lowerExpression(stmt.expression);
    const body = this.lowerStatement(stmt.statement);
    return [{ kind: "ForStmt", variable, iterable, body }];
  }

  lowerSwitch(stmt) {
    this.error(stmt, "switch is limited; use if/else.");
    return [{ kind: "PassStmt" }];
  }

  lowerLValue(node) {
    if (ts.isIdentifier(node)) {
      return { kind: "NameExpr", name: node.text };
    }
    if (ts.isPropertyAccessExpression(node)) {
      return this.lowerPropertyAccess(node, true);
    }
    if (ts.isElementAccessExpression(node)) {
      return {
        kind: "IndexExpr",
        target: this.lowerExpression(node.expression),
        index: this.lowerExpression(node.argumentExpression),
      };
    }
    this.error(node, "Unsupported assignment target.");
    return { kind: "NameExpr", name: "_" };
  }

  lowerExpression(node) {
    if (!node) return { kind: "LiteralExpr", text: "None" };

    // strip as / non-null
    if (ts.isAsExpression(node) || ts.isNonNullExpression(node)) {
      return this.lowerExpression(node.expression);
    }
    // Legacy type assertion: <T>expr
    if (node.kind === ts.SyntaxKind.TypeAssertionExpression) {
      return this.lowerExpression(node.expression);
    }
    if (ts.isParenthesizedExpression(node)) {
      return this.lowerExpression(node.expression);
    }
    if (ts.isIdentifier(node)) {
      return this.lowerIdentifier(node);
    }
    if (ts.isNumericLiteral(node)) {
      return { kind: "LiteralExpr", text: node.text };
    }
    if (ts.isStringLiteral(node) || ts.isNoSubstitutionTemplateLiteral(node)) {
      return { kind: "LiteralExpr", text: JSON.stringify(node.text) };
    }
    if (node.kind === ts.SyntaxKind.TrueKeyword) {
      return { kind: "LiteralExpr", text: "True" };
    }
    if (node.kind === ts.SyntaxKind.FalseKeyword) {
      return { kind: "LiteralExpr", text: "False" };
    }
    if (node.kind === ts.SyntaxKind.NullKeyword || node.kind === ts.SyntaxKind.UndefinedKeyword) {
      return { kind: "LiteralExpr", text: "None" };
    }
    if (ts.isTemplateExpression(node)) {
      return this.lowerTemplate(node);
    }
    if (ts.isArrayLiteralExpression(node)) {
      // detect tuple vs list — treat as list; empty array = []
      return {
        kind: "ListExpr",
        elements: node.elements.map((e) => this.lowerExpression(e)),
      };
    }
    if (ts.isObjectLiteralExpression(node)) {
      return this.lowerObjectLiteral(node);
    }
    if (ts.isNewExpression(node)) {
      return this.lowerNew(node);
    }
    if (ts.isCallExpression(node)) {
      return this.lowerCall(node);
    }
    if (ts.isPropertyAccessExpression(node)) {
      return this.lowerPropertyAccess(node, false);
    }
    if (ts.isElementAccessExpression(node)) {
      return {
        kind: "IndexExpr",
        target: this.lowerExpression(node.expression),
        index: this.lowerExpression(node.argumentExpression),
      };
    }
    if (ts.isBinaryExpression(node)) {
      return this.lowerBinary(node);
    }
    if (ts.isPrefixUnaryExpression(node)) {
      return this.lowerPrefixUnary(node);
    }
    if (ts.isPostfixUnaryExpression(node)) {
      // value of ++x in expression context — not fully supported; emit x
      this.error(node, "++/-- as expression values are not supported; use as statements.");
      return this.lowerExpression(node.operand);
    }
    if (ts.isConditionalExpression(node)) {
      return this.lowerTernary(node);
    }
    if (ts.isArrowFunction(node) || ts.isFunctionExpression(node)) {
      this.error(node, "Lambdas / function expressions are not supported.");
      return { kind: "LiteralExpr", text: "None" };
    }
    if (ts.isThisKeyword?.(node) || node.kind === ts.SyntaxKind.ThisKeyword) {
      return { kind: "NameExpr", name: "self" };
    }
    if (ts.isAwaitExpression(node)) {
      this.error(node, "async/await is not supported.");
      return this.lowerExpression(node.expression);
    }

    this.error(node, `Unsupported expression: ${ts.SyntaxKind[node.kind]}`);
    return { kind: "LiteralExpr", text: "None" };
  }

  lowerIdentifier(node) {
    const name = node.text;
    if (this.localEnumAliases.has(name)) {
      // bare enum type name used as value? unusual
      return { kind: "NameExpr", name };
    }
    if (this.tfwrLocals.has(name)) {
      const orig = this._tfwrLocalToOriginal?.get(name) ?? name;
      if (GAME_ENUMS.has(orig)) {
        return { kind: "NameExpr", name: orig };
      }
      // builtin used as value (rare)
      return { kind: "NameExpr", name: camelOrPascalToSnake(orig) };
    }
    return { kind: "NameExpr", name };
  }

  lowerPropertyAccess(node, asLValue) {
    // Direction.North / Entities.Bush
    if (ts.isIdentifier(node.expression)) {
      const left = node.expression.text;
      const enumName = this.localEnumAliases.get(left) ?? (GAME_ENUMS.has(left) ? left : null);
      if (enumName) {
        const member = node.name.text;
        if (enumName === "Direction") {
          return { kind: "NameExpr", name: member };
        }
        return {
          kind: "AttributeExpr",
          target: { kind: "NameExpr", name: enumName },
          name: member,
        };
      }
      if (this.tfwrNamespace && left === this.tfwrNamespace) {
        const member = node.name.text;
        if (GAME_ENUMS.has(member)) {
          return { kind: "NameExpr", name: member };
        }
        return { kind: "NameExpr", name: camelOrPascalToSnake(member) };
      }
    }

    // this.X → self["X"]
    if (node.expression.kind === ts.SyntaxKind.ThisKeyword) {
      return {
        kind: "IndexExpr",
        target: { kind: "NameExpr", name: "self" },
        index: { kind: "LiteralExpr", text: JSON.stringify(node.name.text) },
      };
    }

    // obj.field for known class instances → obj["field"] when property
    const target = this.lowerExpression(node.expression);
    const prop = node.name.text;

    // Collection/method will be handled in call; for field read on dict-like:
    // Heuristic: if target is NameExpr and we're accessing a property that isn't a module
    const sym = this.checker.getSymbolAtLocation(node);
    if (sym) {
      const flags = sym.getFlags();
      if (flags & ts.SymbolFlags.Method || flags & ts.SymbolFlags.Function) {
        // method reference without call — unsupported
        this.error(node, "Method references without call are not supported.");
        return { kind: "LiteralExpr", text: "None" };
      }
      if (flags & ts.SymbolFlags.Property || flags & ts.SymbolFlags.Field) {
        return {
          kind: "IndexExpr",
          target,
          index: { kind: "LiteralExpr", text: JSON.stringify(prop) },
        };
      }
    }

    // fallback attribute (module.fn)
    return { kind: "AttributeExpr", target, name: prop };
  }

  lowerCall(node) {
    const args = node.arguments.map((a) => this.lowerExpression(a));

    // direct tfwr builtin: harvest()
    if (ts.isIdentifier(node.expression)) {
      const name = node.expression.text;
      if (this.tfwrLocals.has(name) || GAME_BUILTINS.has(name)) {
        const orig = this._tfwrLocalToOriginal?.get(name) ?? name;
        return {
          kind: "CallExpr",
          callee: { kind: "NameExpr", name: camelOrPascalToSnake(orig) },
          arguments: args,
        };
      }
      // local / imported function
      const sym = this.checker.getSymbolAtLocation(node.expression);
      let resolved = sym;
      if (sym && (sym.getFlags() & ts.SymbolFlags.Alias) !== 0) {
        resolved = this.checker.getAliasedSymbol(sym);
      }
      let mod = resolved ? this.symbolModuleMap.get(resolved) : null;
      if (!mod && sym) mod = this.symbolModuleMap.get(sym);

      if (mod && mod !== this.moduleName) {
        this.imports.add(mod);
        return {
          kind: "CallExpr",
          callee: {
            kind: "AttributeExpr",
            target: { kind: "NameExpr", name: mod },
            name,
          },
          arguments: args,
        };
      }

      return {
        kind: "CallExpr",
        callee: { kind: "NameExpr", name },
        arguments: args,
      };
    }

    // ns.member()
    if (ts.isPropertyAccessExpression(node.expression)) {
      const recv = node.expression.expression;
      const method = node.expression.name.text;

      // tfwr namespace
      if (ts.isIdentifier(recv) && this.tfwrNamespace && recv.text === this.tfwrNamespace) {
        return {
          kind: "CallExpr",
          callee: { kind: "NameExpr", name: camelOrPascalToSnake(method) },
          arguments: args,
        };
      }

      // Static Class.method — identifier resolves to a class symbol
      if (ts.isIdentifier(recv)) {
        let idSym = this.checker.getSymbolAtLocation(recv);
        if (idSym && (idSym.getFlags() & ts.SymbolFlags.Alias) !== 0) {
          idSym = this.checker.getAliasedSymbol(idSym);
        }
        if (idSym && (idSym.getFlags() & ts.SymbolFlags.Class) !== 0) {
          const typeName = idSym.getName();
          const fnName = `${typeName}_${method}`;
          let callee = { kind: "NameExpr", name: fnName };
          const mod = this.symbolModuleMap.get(idSym);
          if (mod && mod !== this.moduleName) {
            this.imports.add(mod);
            callee = {
              kind: "AttributeExpr",
              target: { kind: "NameExpr", name: mod },
              name: fnName,
            };
          }
          return { kind: "CallExpr", callee, arguments: args };
        }
      }

      // instance method: p.Move(3,4) → Point_Move(p, 3, 4)
      const receiver = this.lowerExpression(recv);
      const recvType = this.checker.getTypeAtLocation(recv);
      let typeName = recvType?.getSymbol?.()?.getName?.() ?? recvType?.symbol?.getName?.();

      // collection methods
      const coll = this.tryLowerCollectionMethod(method, receiver, args);
      if (coll) return coll;

      if (typeName && typeName !== "__object" && typeName !== "Array" && typeName !== "Object") {
        const fnName = `${typeName}_${method}`;
        let callee = { kind: "NameExpr", name: fnName };
        const tsym = recvType.getSymbol?.() ?? recvType.symbol;
        const mod = tsym ? this.symbolModuleMap.get(tsym) : null;
        if (mod && mod !== this.moduleName) {
          this.imports.add(mod);
          callee = {
            kind: "AttributeExpr",
            target: { kind: "NameExpr", name: mod },
            name: fnName,
          };
        }
        return {
          kind: "CallExpr",
          callee,
          arguments: [receiver, ...args],
        };
      }

      // fallback: attribute call
      return {
        kind: "CallExpr",
        callee: {
          kind: "AttributeExpr",
          target: receiver,
          name: method,
        },
        arguments: args,
      };
    }

    const callee = this.lowerExpression(node.expression);
    return { kind: "CallExpr", callee, arguments: args };
  }

  tryLowerCollectionMethod(method, receiver, args) {
    const map = {
      push: "append",
      add: null, // set.add or list — use add for set, append heuristic
      includes: null,
      has: null,
      pop: "pop",
      insert: null,
    };

    if (method === "push" || method === "append") {
      return {
        kind: "CallExpr",
        callee: { kind: "AttributeExpr", target: receiver, name: "append" },
        arguments: args,
      };
    }
    if (method === "add") {
      return {
        kind: "CallExpr",
        callee: { kind: "AttributeExpr", target: receiver, name: "add" },
        arguments: args,
      };
    }
    if (method === "includes" || method === "has" || method === "contains" || method === "Contains") {
      // x in set
      return {
        kind: "BinaryExpr",
        left: args[0],
        op: "in",
        right: receiver,
      };
    }
    if (method === "pop") {
      return {
        kind: "CallExpr",
        callee: { kind: "AttributeExpr", target: receiver, name: "pop" },
        arguments: args,
      };
    }
    return null;
  }

  lowerNew(node) {
    if (!node.expression) {
      this.error(node, "Unsupported new expression.");
      return { kind: "LiteralExpr", text: "None" };
    }

    // new Set([...]) / new Map([...]) / new Array
    if (ts.isIdentifier(node.expression)) {
      const name = node.expression.text;
      if (name === "Set") {
        if (node.arguments?.[0] && ts.isArrayLiteralExpression(node.arguments[0])) {
          return {
            kind: "SetExpr",
            elements: node.arguments[0].elements.map((e) => this.lowerExpression(e)),
          };
        }
        return { kind: "SetExpr", elements: [] };
      }
      if (name === "Map") {
        // new Map([[k,v],...]) or empty
        if (node.arguments?.[0] && ts.isArrayLiteralExpression(node.arguments[0])) {
          const entries = [];
          for (const el of node.arguments[0].elements) {
            if (ts.isArrayLiteralExpression(el) && el.elements.length >= 2) {
              entries.push({
                key: this.lowerExpression(el.elements[0]),
                value: this.lowerExpression(el.elements[1]),
              });
            }
          }
          return { kind: "DictExpr", entries };
        }
        return { kind: "DictExpr", entries: [] };
      }
      if (name === "Array") {
        const args = (node.arguments ?? []).map((a) => this.lowerExpression(a));
        return { kind: "ListExpr", elements: args };
      }

      // User class
      const args = (node.arguments ?? []).map((a) => this.lowerExpression(a));
      const fnName = `${name}_new`;
      let callee = { kind: "NameExpr", name: fnName };
      const sym = this.checker.getSymbolAtLocation(node.expression);
      const mod = sym ? this.symbolModuleMap.get(sym) : null;
      if (mod && mod !== this.moduleName) {
        this.imports.add(mod);
        callee = {
          kind: "AttributeExpr",
          target: { kind: "NameExpr", name: mod },
          name: fnName,
        };
      }
      return { kind: "CallExpr", callee, arguments: args };
    }

    this.error(node, "Unsupported new expression.");
    return { kind: "LiteralExpr", text: "None" };
  }

  lowerObjectLiteral(node) {
    // {} as dict, or { a: 1 } 
    const entries = [];
    for (const prop of node.properties) {
      if (ts.isPropertyAssignment(prop)) {
        let key;
        if (ts.isIdentifier(prop.name)) {
          key = { kind: "LiteralExpr", text: JSON.stringify(prop.name.text) };
        } else if (ts.isStringLiteral(prop.name)) {
          key = { kind: "LiteralExpr", text: JSON.stringify(prop.name.text) };
        } else if (ts.isComputedPropertyName(prop.name)) {
          key = this.lowerExpression(prop.name.expression);
        } else {
          this.error(prop, "Unsupported object key.");
          continue;
        }
        entries.push({ key, value: this.lowerExpression(prop.initializer) });
      } else {
        this.error(prop, "Unsupported object literal member.");
      }
    }
    return { kind: "DictExpr", entries };
  }

  lowerTemplate(node) {
    // head + str(expr) + lit + ...
    let expr = { kind: "LiteralExpr", text: JSON.stringify(node.head.text) };
    for (const span of node.templateSpans) {
      const part = {
        kind: "CallExpr",
        callee: { kind: "NameExpr", name: "str" },
        arguments: [this.lowerExpression(span.expression)],
      };
      expr = { kind: "BinaryExpr", left: expr, op: "+", right: part };
      const lit = { kind: "LiteralExpr", text: JSON.stringify(span.literal.text) };
      expr = { kind: "BinaryExpr", left: expr, op: "+", right: lit };
    }
    return expr;
  }

  lowerBinary(node) {
    const kind = node.operatorToken.kind;
    if (kind === ts.SyntaxKind.EqualsToken) {
      // assignment as expression — hoist
      if (!this.hoist) {
        this.error(node, "Assignment expressions require a statement context.");
        return this.lowerExpression(node.right);
      }
      const value = this.lowerExpression(node.right);
      const target = this.lowerLValue(node.left);
      this.hoist.push({ kind: "AssignStmt", target, value });
      return target;
    }

    if (isCompoundAssign(kind)) {
      if (!this.hoist) {
        this.error(node, "Compound assignment expressions require a statement context.");
        return this.lowerExpression(node.left);
      }
      const value = this.lowerExpression(node.right);
      const target = this.lowerLValue(node.left);
      this.hoist.push({ kind: "AugAssignStmt", target, op: compoundOp(kind), value });
      return target;
    }

    const left = this.lowerExpression(node.left);
    const right = this.lowerExpression(node.right);
    const op = binaryOp(kind);
    if (!op) {
      this.error(node, `Binary operator '${node.operatorToken.getText()}' is not supported.`);
      return left;
    }
    return { kind: "BinaryExpr", left, op, right };
  }

  lowerPrefixUnary(node) {
    if (node.operator === ts.SyntaxKind.ExclamationToken) {
      return { kind: "UnaryExpr", op: "not", operand: this.lowerExpression(node.operand) };
    }
    if (node.operator === ts.SyntaxKind.MinusToken) {
      return { kind: "UnaryExpr", op: "-", operand: this.lowerExpression(node.operand) };
    }
    if (node.operator === ts.SyntaxKind.PlusToken) {
      return this.lowerExpression(node.operand);
    }
    if (node.operator === ts.SyntaxKind.PlusPlusToken || node.operator === ts.SyntaxKind.MinusMinusToken) {
      this.error(node, "++/-- as expression values are not supported; use as statements.");
      return this.lowerExpression(node.operand);
    }
    this.error(node, "Unsupported unary operator.");
    return this.lowerExpression(node.operand);
  }

  lowerTernary(node) {
    if (!this.hoist) {
      this.error(node, "Ternary expressions require a statement context.");
      return this.lowerExpression(node.whenTrue);
    }
    const temp = this.newTemp("tern");
    const cond = this.lowerExpression(node.condition);
    const whenTrue = this.lowerExpression(node.whenTrue);
    const whenFalse = this.lowerExpression(node.whenFalse);
    this.hoist.push({
      kind: "IfStmt",
      condition: cond,
      thenBody: [{
        kind: "AssignStmt",
        target: { kind: "NameExpr", name: temp },
        value: whenTrue,
      }],
      elifs: [],
      elseBody: [{
        kind: "AssignStmt",
        target: { kind: "NameExpr", name: temp },
        value: whenFalse,
      }],
    });
    return { kind: "NameExpr", name: temp };
  }
}

function isCompoundAssign(kind) {
  return (
    kind === ts.SyntaxKind.PlusEqualsToken ||
    kind === ts.SyntaxKind.MinusEqualsToken ||
    kind === ts.SyntaxKind.AsteriskEqualsToken ||
    kind === ts.SyntaxKind.SlashEqualsToken ||
    kind === ts.SyntaxKind.PercentEqualsToken
  );
}

function compoundOp(kind) {
  switch (kind) {
    case ts.SyntaxKind.PlusEqualsToken: return "+";
    case ts.SyntaxKind.MinusEqualsToken: return "-";
    case ts.SyntaxKind.AsteriskEqualsToken: return "*";
    case ts.SyntaxKind.SlashEqualsToken: return "/";
    case ts.SyntaxKind.PercentEqualsToken: return "%";
    default: return "+";
  }
}

function binaryOp(kind) {
  switch (kind) {
    case ts.SyntaxKind.PlusToken: return "+";
    case ts.SyntaxKind.MinusToken: return "-";
    case ts.SyntaxKind.AsteriskToken: return "*";
    case ts.SyntaxKind.SlashToken: return "/";
    case ts.SyntaxKind.PercentToken: return "%";
    case ts.SyntaxKind.EqualsEqualsToken:
    case ts.SyntaxKind.EqualsEqualsEqualsToken: return "==";
    case ts.SyntaxKind.ExclamationEqualsToken:
    case ts.SyntaxKind.ExclamationEqualsEqualsToken: return "!=";
    case ts.SyntaxKind.LessThanToken: return "<";
    case ts.SyntaxKind.LessThanEqualsToken: return "<=";
    case ts.SyntaxKind.GreaterThanToken: return ">";
    case ts.SyntaxKind.GreaterThanEqualsToken: return ">=";
    case ts.SyntaxKind.AmpersandAmpersandToken: return "and";
    case ts.SyntaxKind.BarBarToken: return "or";
    case ts.SyntaxKind.InKeyword: return "in";
    default: return null;
  }
}

// Fix lowerIf to return pre statements — patch lowerStatement for if
const _origLowerStatement = LoweringContext.prototype.lowerStatement;
LoweringContext.prototype.lowerStatement = function (stmt) {
  if (ts.isIfStatement(stmt)) {
    this._pendingPre = null;
    const ifStmt = this.lowerIf(stmt);
    const pre = this._pendingPre ?? [];
    this._pendingPre = null;
    return [...pre, ifStmt];
  }
  return _origLowerStatement.call(this, stmt);
};

main();
