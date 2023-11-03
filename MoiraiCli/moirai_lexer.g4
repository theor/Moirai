lexer grammar moirai_lexer;

STRING : ('"' (~[\\"])* '"') | ('\''(~[\\'])*  '\'');
NULL: 'null';
SPACE: [ \t]+ -> skip;
LINE_BREAK: ('\r\n' | '\r' | '\n');
COMMENT
  :  '#' ~( '\r' | '\n' )* LINE_BREAK -> skip
  ;

COLON: ':';
SCOPE_OPEN: '{';
SCOPE_CLOSE: '}';
PAREN_OPEN: '(';
PAREN_CLOSE: ')';
RULE: 'rule';
ENTITY: 'entity';
EVENT: 'event';
PROP: 'prop';
ENUM: 'enum';
WHEN: 'when';
SET: 'set';
COMMA: ',';

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

VAR_ID: '$' (ALPHA|DIGIT)(ALPHA|DIGIT|'_')*;
PROP_ID: '%' [a-z][a-z_]*;

AT : '@' ;
TYPE_ID : ALPHA_UPPER (ALPHA|'_')* ;
ID : ALPHA_LOWER (ALPHA|'_'|DIGIT)* ;
NUMBER: DIGIT+ ;
fragment
DIGIT   :   ('0'..'9');
fragment
ALPHA   :   ('a'..'z'|'A'..'Z');
fragment
ALPHA_UPPER   :   ('A'..'Z');
fragment
ALPHA_LOWER   :   ('a'..'z');