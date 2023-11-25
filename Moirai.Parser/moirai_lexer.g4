lexer grammar moirai_lexer;

channels {
    COMMENTS
}
STRING : ('"' (~[\\"])* '"') | ('\''(~[\\'])*  '\'');
NULL: 'null';
SPACE: [ \t]+ -> channel(HIDDEN);
LINE_BREAK: ('\r\n' | '\r' | '\n');
COMMENT
  :  '//' ~( '\r' | '\n')* -> channel(COMMENTS);

COLON_EQ: ':=';
COLON: ':';
SCOPE_OPEN: '{';
SCOPE_CLOSE: '}';
PAREN_OPEN: '(';
PAREN_CLOSE: ')';
EVENT: 'event';
ENTITY: 'entity';
TRIGGER: 'trigger';
//NAME: 'name';
PROP: 'prop';
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
ADD: '+';
SUB: '-';
MUL: '*';
DIV: '/';
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
