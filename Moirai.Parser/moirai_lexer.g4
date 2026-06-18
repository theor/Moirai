lexer grammar moirai_lexer;

channels {
    COMMENTS
}

QUOTE: '\'' -> pushMode(IN_STRING);
//STRING : ('"' (~[\\"])* '"') | ('\''(~[\\'])*  '\'');
NULL: 'null';
SPACE: [ \t]+ -> channel(HIDDEN);
LINE_BREAK: ('\r\n' | '\r' | '\n');
COMMENT
  :  '//' ~( '\r' | '\n')* -> channel(COMMENTS);

COLON_EQ: ':=';
COLON: ':';
SCOPE_OPEN: '{' ->pushMode(DEFAULT_MODE);
SCOPE_CLOSE: '}' -> popMode;
PAREN_OPEN: '(';
PAREN_CLOSE: ')';
LBRACK: '[';
RBRACK: ']';
EVENT: 'event';
ENTITY: 'entity';
TRIGGER: 'trigger';
//NAME: 'name';
PROP: 'prop';
FUNCTION: 'function';
ENUM: 'enum';
WHEN: 'when';
WHEN_CREATED: 'when_created';
SET: 'set';
VAR: 'var';
MATCH: 'match';
MATCH_WEIGHT: 'random_weighted';
COMMA: ',';
ARROW: '=>';
IF: 'if';
ELSE: 'else';

TRUE: 'true';
FALSE: 'false';
DOT: '.' ;
NEQ: '!=' ;
EQ: '=';
QQ: '??';
ADD: '+';
SUB: '-';
MUL: '*';
DIV: '/';
MOD: '%';
GE: '>=';
LE: '<=';
GT: '>';
LT: '<';
AND: 'and';
OR: 'or';

SINGLETON_ID: '#' (ALPHA_UPPER)(ALPHA|'_')*;
VAR_ID: '$' (ALPHA|DIGIT)(ALPHA|DIGIT|'_')*;
PROP_ID: '%' [a-z][a-z_]*;

AT : '@' ;
TYPE_ID : ALPHA_UPPER (ALPHA|'_')* ;
ID : (ALPHA_LOWER|'_') (ALPHA|'_'|DIGIT)* ;
PERCENT: '-'?DIGIT+'%' ;
NUMBER_FLOAT: '-'?DIGIT+'.'DIGIT+ ;
NUMBER: '-'?DIGIT+ ;
    
fragment
DIGIT   :   ('0'..'9');
fragment
ALPHA   :   ('a'..'z'|'A'..'Z');
fragment
ALPHA_UPPER   :   ('A'..'Z');
fragment
ALPHA_LOWER   :   ('a'..'z');

mode IN_STRING;
fragment
QUOTED_QUOTE: '\\\'';
TEXT: (QUOTED_QUOTE | ~['{])+ ;

EXPR_OPEN: '{' -> pushMode(DEFAULT_MODE);
QUOTE_IN_STRING: '\'' -> type(QUOTE), popMode;
